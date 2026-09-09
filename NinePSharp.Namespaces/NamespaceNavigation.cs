using NinePSharp.Constants;

namespace NinePSharp.Namespaces;

/// <summary>Routes operations to the provider named by a resource handle.</summary>
public interface IResourceOperations
{
    /// <summary>Walks one path component from a directory.</summary>
    ValueTask<ResourceHandle?> WalkAsync(ResourceHandle directory, string name, CancellationToken cancellationToken);

    /// <summary>Reads all entries from a directory.</summary>
    ValueTask<IReadOnlyList<ResourceDirectoryEntry>> ReadDirectoryAsync(
        ResourceHandle directory,
        CancellationToken cancellationToken);

    /// <summary>Creates a child in a directory.</summary>
    ValueTask<ResourceHandle> CreateAsync(
        ResourceHandle directory,
        string name,
        bool directoryEntry,
        CancellationToken cancellationToken);
}

/// <summary>One visible location retained by a channel during path traversal.</summary>
/// <param name="Name">The visible path element.</param>
/// <param name="Handle">The selected resource.</param>
/// <param name="MountedFrom">The hidden mounted-upon resource, when this frame crossed a mount.</param>
/// <param name="Union">The ordered union visible at this location.</param>
public sealed record ChannelFrame(
    string Name,
    ResourceHandle Handle,
    ResourceHandle? MountedFrom = null,
    IReadOnlyList<MountBinding>? Union = null);

/// <summary>An object-based channel which retains visible traversal and mount crossings.</summary>
public sealed class NamespaceChannel
{
    private readonly List<ChannelFrame> frames;

    internal NamespaceChannel(IEnumerable<ChannelFrame> frames)
    {
        this.frames = frames.ToList();
        if (this.frames.Count == 0)
        {
            throw new ArgumentException("A channel must contain a root frame.", nameof(frames));
        }
    }

    /// <summary>Gets the currently selected resource.</summary>
    public ResourceHandle Current => frames[^1].Handle;

    /// <summary>Gets the current visible path.</summary>
    public IReadOnlyList<string> VisiblePath => frames.Skip(1).Select(frame => frame.Name).ToArray();

    /// <summary>Gets an immutable view of the navigation history.</summary>
    public IReadOnlyList<ChannelFrame> Frames => frames.ToArray();

    /// <summary>Creates an independent channel retaining the same object references.</summary>
    public NamespaceChannel Clone() => new(frames);

    /// <summary>Restores a channel from persisted traversal frames.</summary>
    public static NamespaceChannel Restore(IEnumerable<ChannelFrame> frames)
        => new NamespaceChannel(frames);

    internal ChannelFrame CurrentFrame => frames[^1];

    internal void Push(ChannelFrame frame) => frames.Add(frame);

    internal void ReplaceCurrent(ChannelFrame frame) => frames[^1] = frame;

    internal void WalkParent()
    {
        if (frames.Count > 1)
        {
            frames.RemoveAt(frames.Count - 1);
        }
    }
}

/// <summary>The result of a potentially partial multi-element walk.</summary>
/// <param name="Channel">The channel at the last successful element.</param>
/// <param name="Qids">Qids for successful path elements.</param>
public sealed record NamespaceWalkResult(NamespaceChannel Channel, IReadOnlyList<Qid> Qids)
{
    /// <summary>Gets whether every requested element was walked.</summary>
    public bool Complete(int requestedElements) => Qids.Count == requestedElements;
}

/// <summary>Applies a mount table while navigating mountable resource providers.</summary>
public sealed class NamespaceNavigator
{
    private readonly MountTable mounts;
    private readonly IResourceOperations resources;

    /// <summary>Creates a navigator over one virtual process group's namespace.</summary>
    public NamespaceNavigator(MountTable mounts, IResourceOperations resources)
    {
        this.mounts = mounts ?? throw new ArgumentNullException(nameof(mounts));
        this.resources = resources ?? throw new ArgumentNullException(nameof(resources));
    }

    /// <summary>Attaches a channel to a root and crosses a root mount if present.</summary>
    public NamespaceChannel Attach(ResourceHandle root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var channel = new NamespaceChannel(new[] { new ChannelFrame("/", root) });
        CrossMount(channel);
        return channel;
    }

    /// <summary>Binds the current channel onto another resource, copying a source union when present.</summary>
    public MountBinding Mount(
        NamespaceChannel source,
        ResourceHandle mountedOn,
        MountFlags flags = MountFlags.Replace,
        string? spec = null)
        => mounts.Mount(source, mountedOn, flags, spec);

    /// <summary>Walks path elements with Plan 9 replacement and union fallback behavior.</summary>
    public async ValueTask<NamespaceWalkResult> WalkAsync(
        NamespaceChannel source,
        IReadOnlyList<string> names,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(names);

        NamespaceChannel channel = source.Clone();
        var qids = new List<Qid>();

        foreach (string name in names)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (name is null)
            {
                throw new ArgumentException("Walk elements cannot be null.", nameof(names));
            }

            if (name.Length == 0 || name == ".")
            {
                qids.Add(channel.Current.Qid);
                continue;
            }

            if (name == "..")
            {
                channel.WalkParent();
                qids.Add(channel.Current.Qid);
                continue;
            }

            ResourceHandle? child = await WalkUnionAsync(channel.CurrentFrame, name, cancellationToken);
            if (child is null)
            {
                break;
            }

            channel.Push(new ChannelFrame(name, child));
            CrossMount(channel);
            qids.Add(channel.Current.Qid);
        }

        return new NamespaceWalkResult(channel, qids);
    }

    /// <summary>Reads a directory, concatenating union members in mount order.</summary>
    public async ValueTask<IReadOnlyList<ResourceDirectoryEntry>> ReadDirectoryAsync(
        NamespaceChannel channel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ChannelFrame frame = channel.CurrentFrame;
        IReadOnlyList<MountBinding>? union = ResolveMounts(frame);
        IEnumerable<ResourceHandle> members = union?.Select(binding => binding.Target) ?? new[] { frame.Handle };
        var entries = new List<ResourceDirectoryEntry>();

        foreach (ResourceHandle member in members)
        {
            IReadOnlyList<ResourceDirectoryEntry> memberEntries =
                await resources.ReadDirectoryAsync(member, cancellationToken);
            entries.AddRange(memberEntries.Select(ApplyMountedStat));
        }

        return entries;
    }

    /// <summary>Creates in the selected MCREATE union member or the ordinary directory.</summary>
    public async ValueTask<ResourceHandle> CreateAsync(
        NamespaceChannel channel,
        string name,
        bool directory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ChannelFrame frame = channel.CurrentFrame;
        IReadOnlyList<MountBinding>? union = ResolveMounts(frame);
        ResourceHandle target;
        if (union is null)
        {
            target = frame.Handle;
        }
        else if (frame.MountedFrom is not null)
        {
            target = mounts.SelectCreateTarget(frame.MountedFrom.Identity);
        }
        else
        {
            target = union.FirstOrDefault(binding => (binding.Flags & MountFlags.Create) != 0)?.Target
                ?? throw new NamespaceException(
                    NamespaceError.CreateNotPermitted,
                    "No member of the mounted union permits creation.");
        }

        return await resources.CreateAsync(target, name, directory, cancellationToken);
    }

    private async ValueTask<ResourceHandle?> WalkUnionAsync(
        ChannelFrame frame,
        string name,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<MountBinding>? union = ResolveMounts(frame);
        if (union is null)
        {
            return await resources.WalkAsync(frame.Handle, name, cancellationToken);
        }

        foreach (MountBinding binding in union)
        {
            ResourceHandle? result = await resources.WalkAsync(binding.Target, name, cancellationToken);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    private void CrossMount(NamespaceChannel channel)
    {
        ChannelFrame frame = channel.CurrentFrame;
        MountHead? head = mounts.Find(frame.Handle.Identity);
        if (head is null || head.Mounts.Count == 0)
        {
            return;
        }

        channel.ReplaceCurrent(new ChannelFrame(frame.Name, head.Mounts[0].Target, frame.Handle, head.Mounts));
    }

    private ResourceDirectoryEntry ApplyMountedStat(ResourceDirectoryEntry entry)
    {
        MountHead? head = mounts.Find(entry.Handle.Identity);
        if (head is null)
        {
            return entry;
        }

        return entry with { Handle = head.Mounts[0].Target };
    }

    private IReadOnlyList<MountBinding>? ResolveMounts(ChannelFrame frame)
    {
        if (frame.MountedFrom is null)
        {
            return frame.Union;
        }

        return mounts.Find(frame.MountedFrom.Identity)?.Mounts;
    }
}
