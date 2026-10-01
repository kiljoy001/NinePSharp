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

    internal void UpdateCurrent(ResourceHandle handle)
    {
        ChannelFrame current = CurrentFrame;
        ReplaceCurrent(current with { Handle = handle });
    }

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
    public ValueTask<NamespaceWalkResult> WalkAsync(
        NamespaceChannel source,
        IReadOnlyList<string> names,
        CancellationToken cancellationToken = default)
        => WalkAsync(source, names, true, cancellationToken);

    // Amount in 9front uses the ordinary union walk, but leaves its final
    // resource on the mounted-upon side. Abind crosses that final mount too.
    internal async ValueTask<NamespaceWalkResult> WalkAsync(
        NamespaceChannel source,
        IReadOnlyList<string> names,
        bool crossFinalMount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(names);
        mounts.EnsureOpen();

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
            if (crossFinalMount || qids.Count < names.Count - 1)
            {
                CrossMount(channel);
            }

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
        ResourceHandle target = SelectCreateTarget(channel);
        return await resources.CreateAsync(target, name, directory, cancellationToken);
    }

    /// <summary>Returns the ordinary directory or first union member marked for creation.</summary>
    public ResourceHandle SelectCreateTarget(NamespaceChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ChannelFrame frame = channel.CurrentFrame;
        IReadOnlyList<MountBinding>? union = ResolveMounts(frame);
        if (union is null)
        {
            return frame.Handle;
        }

        // Use the resolved union consistently; a second mount-table lookup could
        // select from a different snapshot after a concurrent namespace change.
        return union.FirstOrDefault(binding => (binding.Flags & MountFlags.Create) != 0)?.Target
            ?? throw new NamespaceException(
                NamespaceError.CreateNotPermitted,
                "No member of the mounted union permits creation.");
    }

    /// <summary>Creates the replacement channel for a child returned by a successful create.</summary>
    public NamespaceChannel EnterCreated(
        NamespaceChannel parent,
        string name,
        ResourceHandle created)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(created);
        NamespaceChannel channel = parent.Clone();
        channel.Push(new ChannelFrame(name, created));
        // chan.c:Acreate returns the newly created channel directly. Re-entering
        // mount resolution here could redirect it or lose an owned handle on exit.
        return channel;
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
            // Root/current-directory channels can remain on the mounted-upon side.
            // Resolve a new mount there before walking or choosing a create member.
            return mounts.Find(frame.Handle.Identity)?.Mounts ?? frame.Union;
        }

        return mounts.Find(frame.MountedFrom.Identity)?.Mounts;
    }
}
