using NinePSharp.Constants;

namespace NinePSharp.Namespaces;

/// <summary>Applies a mount table while navigating mountable resource providers.</summary>
public sealed class NamespaceNavigator
{
    private readonly MountTable mounts;
    private readonly IResourceOperations resources;

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
