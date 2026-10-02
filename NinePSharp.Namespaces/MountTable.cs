namespace NinePSharp.Namespaces;

/// <summary>
/// Models the mount table held by a Plan 9 process group.
/// </summary>
public sealed partial class MountTable
{
    private readonly object gate = new();
    private readonly Dictionary<ResourceIdentity, MountHead> heads = new();
    private readonly HashSet<string> blockedMountDevices = new(StringComparer.Ordinal);
    private long nextMountId;
    private bool mountsDisabled;
    private bool closed;

    /// <summary>Gets a value indicating whether the last namespace owner released this table.</summary>
    public bool IsClosed
    {
        get
        {
            lock (gate)
{
    return closed;
}
        }
    }

    /// <summary>Gets a value indicating whether all service mounts are disabled for this namespace.</summary>
    public bool MountsDisabled
    {
        get
        {
            lock (gate)
            {
                return mountsDisabled;
            }
        }
    }

    /// <summary>Gets the blocked service-device names.</summary>
    public IReadOnlyList<string> BlockedMountDevices
    {
        get
        {
            lock (gate)
            {
                return blockedMountDevices.Order(StringComparer.Ordinal).ToArray();
            }
        }
    }

    /// <summary>Restores a mount table from persisted state.</summary>
    public static MountTable FromSnapshot(NamespaceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var table = new MountTable
        {
            nextMountId = snapshot.NextMountId,
            mountsDisabled = snapshot.MountsDisabled,
        };
        if (snapshot.BlockedMountDevices is not null)
        {
            table.blockedMountDevices.UnionWith(snapshot.BlockedMountDevices);
        }

        foreach (MountHead head in snapshot.MountHeads)
        {
            table.heads.Add(head.From.Identity, Copy(head));
        }

        return table;
    }

    /// <summary>Sets whether all service mounts are disabled for this namespace.</summary>
    public void SetMountsDisabled(bool disabled)
    {
        lock (gate)
        {
            EnsureOpen();
            mountsDisabled = disabled;
        }
    }

    /// <summary>Blocks or permits service mounts targeting one device name.</summary>
    public void SetMountDeviceBlocked(string device, bool blocked)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(device);
        lock (gate)
        {
            EnsureOpen();
            if (blocked)
            {
                blockedMountDevices.Add(device);
            }
            else
            {
                blockedMountDevices.Remove(device);
            }
        }
    }

    /// <summary>Adds a replacement or union member at a mounted-upon resource.</summary>
    public MountBinding Mount(
        ResourceHandle target,
        ResourceHandle mountedOn,
        MountFlags flags = MountFlags.Replace,
        string? spec = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(mountedOn);
        EnsureMountAllowed(target);
        if (!mountedOn.IsDirectory && flags.Order() == MountFlags.Replace)
        {
            throw new NamespaceException(
                NamespaceError.MountTargetMustBeDirectory,
                "A service mount target must be a directory.");
        }

        return Mount(target, mountedOn, flags, spec, null);
    }

    /// <summary>Binds a channel, copying its mounted union when present.</summary>
    public MountBinding Mount(
        NamespaceChannel source,
        ResourceHandle mountedOn,
        MountFlags flags = MountFlags.Replace,
        string? spec = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if ((flags & MountFlags.Cache) != 0)
        {
            throw new NamespaceException(
                NamespaceError.InvalidMountFlags,
                "The cache flag is valid only for service mounts.");
        }

        ChannelFrame frame = source.Frames[^1];
        IReadOnlyList<MountBinding>? sourceMounts = frame.MountedFrom is null
            ? frame.Union
            : Find(frame.MountedFrom.Identity)?.Mounts;
        ResourceHandle target = sourceMounts is { Count: > 0 } ? sourceMounts[0].Target : frame.Handle;
        return Mount(target, mountedOn, flags, spec, sourceMounts);
    }

    /// <summary>Removes every mount or one selected union member from a mount point.</summary>
    public void Unmount(ResourceHandle mountedOn, ResourceHandle? mounted = null)
        => UnmountCore(mountedOn, mounted, null);

    /// <summary>Finds the ordered mount list for an object identity.</summary>
    public MountHead? Find(ResourceIdentity identity)
    {
        lock (gate)
        {
            EnsureOpen();
            return heads.TryGetValue(identity, out MountHead? head) ? Copy(head) : null;
        }
    }

    /// <summary>Returns the first union member marked as a creation target.</summary>
    public ResourceHandle SelectCreateTarget(ResourceIdentity identity)
    {
        MountHead? head = Find(identity);
        MountBinding? target = head?.Mounts.FirstOrDefault(binding => (binding.Flags & MountFlags.Create) != 0);
        return target?.Target ?? throw new NamespaceException(
            NamespaceError.CreateNotPermitted,
            "No member of the mounted union permits creation.");
    }

    /// <summary>Creates an independent namespace snapshot with the same mount ordering.</summary>
    public MountTable Clone()
    {
        EnsureOpen();
        NamespaceSnapshot snapshot = Snapshot();
        var clone = new MountTable();
        var ids = snapshot.MountHeads
            .SelectMany(head => head.Mounts)
            .Select(binding => binding.MountId)
            .Distinct()
            .OrderBy(id => id)
            .Select((id, index) => (id, replacement: (long)index + 1))
            .ToDictionary(pair => pair.id, pair => pair.replacement);

        foreach (MountHead head in snapshot.MountHeads)
        {
            clone.heads.Add(
                head.From.Identity,
                new MountHead(
                    head.From,
                    head.Mounts.Select(binding => binding with { MountId = ids[binding.MountId] }).ToArray()));
        }

        clone.nextMountId = ids.Count;
        clone.mountsDisabled = snapshot.MountsDisabled;
        if (snapshot.BlockedMountDevices is not null)
        {
            clone.blockedMountDevices.UnionWith(snapshot.BlockedMountDevices);
        }

        return clone;
    }

    /// <summary>Captures an immutable copy suitable for persistence.</summary>
    public NamespaceSnapshot Snapshot()
    {
        lock (gate)
        {
            return new NamespaceSnapshot(
                nextMountId,
                heads.Values.Select(Copy).ToArray(),
                mountsDisabled,
                blockedMountDevices.Order(StringComparer.Ordinal).ToArray());
        }
    }

    internal void Close()
    {
        lock (gate)
        {
            closed = true;
            foreach (DirectoryMountHead head in directoryHeads.Values)
            {
                _ = head.RetireAsync();
            }

            directoryHeads.Clear();
            heads.Clear();
        }
    }

    internal void EnsureOpen()
    {
        lock (gate)
        {
            if (closed)
            {
                throw new NamespaceException(NamespaceError.NamespaceClosed, "The namespace is closed.");
            }
        }
    }

    internal MountBinding Mount(
        ResourceHandle target,
        ResourceHandle mountedOn,
        MountFlags flags,
        string? spec,
        IReadOnlyList<MountBinding>? sourceMounts,
        DirectoryMountHead? heldHead = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(mountedOn);
        ValidateMount(target, mountedOn, flags);
        ValidateSourceMounts(target, flags, sourceMounts);

        lock (gate)
        {
            EnsureOpen();
            using IDisposable? directoryMutation = EnterDirectoryMutation(mountedOn.Identity, heldHead);
            var newMounts = new List<MountBinding> { NewBinding(target, flags, spec) };
            MountFlags copiedOrder = flags.Order() == MountFlags.Replace ? MountFlags.After : flags.Order();
            if (sourceMounts is not null)
            {
                foreach (MountBinding source in sourceMounts.Skip(1))
                {
                    newMounts.Add(NewBinding(source.Target, copiedOrder, source.Spec));
                }
            }

            MountBinding binding = newMounts[0];
            heads.TryGetValue(mountedOn.Identity, out MountHead? existing);

            if (flags.Order() == MountFlags.Replace)
            {
                heads[mountedOn.Identity] = new MountHead(mountedOn, newMounts.ToArray());
                UpdateDirectoryHead(mountedOn.Identity, heads[mountedOn.Identity].Mounts);
                return binding;
            }

            var mounts = existing?.Mounts.ToList() ?? new List<MountBinding>
            {
                NewBinding(mountedOn, MountFlags.Replace, string.Empty),
            };

            if (flags.Order() == MountFlags.After)
            {
                mounts.AddRange(newMounts);
            }
            else
            {
                mounts.InsertRange(0, newMounts);
            }

            heads[mountedOn.Identity] = new MountHead(mountedOn, mounts.ToArray());
            UpdateDirectoryHead(mountedOn.Identity, heads[mountedOn.Identity].Mounts);
            return binding;
        }
    }

    private static void ValidateMount(ResourceHandle target, ResourceHandle mountedOn, MountFlags flags)
    {
        if ((flags & ~MountFlagsExtensions.All) != 0 || flags.Order() == MountFlagsExtensions.OrderMask)
        {
            throw new NamespaceException(NamespaceError.InvalidMountFlags, "The mount ordering flags are invalid.");
        }

        if (target.IsDirectory != mountedOn.IsDirectory)
        {
            throw new NamespaceException(NamespaceError.MountTypeMismatch, "Mount source and target must have matching object kinds.");
        }

        if (!mountedOn.IsDirectory && flags.Order() != MountFlags.Replace)
        {
            throw new NamespaceException(NamespaceError.UnionRequiresDirectory, "Only directories can form unions.");
        }
    }

    private static void ValidateSourceMounts(
        ResourceHandle target,
        MountFlags flags,
        IReadOnlyList<MountBinding>? sourceMounts)
    {
        if (sourceMounts is null || sourceMounts.Count == 0)
        {
            return;
        }

        if (sourceMounts[0].Target.Identity != target.Identity)
        {
            throw new ArgumentException("The source mount list does not describe the target channel.", nameof(sourceMounts));
        }

        if ((flags & MountFlags.Create) != 0 &&
            (sourceMounts.Count > 1 || (sourceMounts[0].Flags & MountFlags.Create) == 0))
        {
            throw new NamespaceException(
                NamespaceError.CreateBindNotPermitted,
                "A creatable bind requires one mounted source which already permits creation.");
        }
    }

    private static MountHead Copy(MountHead head)
        => new(head.From, head.Mounts.ToArray());

    private void UnmountCore(ResourceHandle mountedOn, ResourceHandle? mounted, DirectoryMountHead? heldHead)
    {
        ArgumentNullException.ThrowIfNull(mountedOn);

        lock (gate)
        {
            EnsureOpen();
            using IDisposable? directoryMutation = EnterDirectoryMutation(mountedOn.Identity, heldHead);
            if (!heads.TryGetValue(mountedOn.Identity, out MountHead? head))
            {
                throw new NamespaceException(NamespaceError.MountNotFound, "The resource is not a mount point.");
            }

            if (mounted is null)
            {
                heads.Remove(mountedOn.Identity);
                UpdateDirectoryHead(mountedOn.Identity, null);
                return;
            }

            var mounts = head.Mounts.ToList();
            int index = mounts.FindIndex(candidate => candidate.Target.Identity == mounted.Identity);
            if (index < 0)
            {
                throw new NamespaceException(NamespaceError.UnionMemberNotFound, "The resource is not in the mounted union.");
            }

            mounts.RemoveAt(index);
            if (mounts.Count == 0)
            {
                heads.Remove(mountedOn.Identity);
                UpdateDirectoryHead(mountedOn.Identity, null);
            }
            else
            {
                heads[mountedOn.Identity] = head with { Mounts = mounts.ToArray() };
                UpdateDirectoryHead(mountedOn.Identity, heads[mountedOn.Identity].Mounts);
            }
        }
    }

    private MountBinding NewBinding(ResourceHandle target, MountFlags flags, string? spec)
        => new(++nextMountId, flags, target, spec ?? string.Empty);

    private void EnsureMountAllowed(ResourceHandle target)
    {
        lock (gate)
        {
            EnsureOpen();
            if (mountsDisabled || blockedMountDevices.Contains(target.Identity.Device))
            {
                throw new NamespaceException(
                    NamespaceError.MountDeviceDenied,
                    "The process namespace is not permitted to mount this device.");
            }
        }
    }
}
