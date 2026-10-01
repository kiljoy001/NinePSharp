namespace NinePSharp.Namespaces;

/// <summary>Controls namespace ownership when a virtual process is forked.</summary>
public enum NamespaceForkMode
{
    /// <summary>The child shares its parent's virtual process group.</summary>
    Share,

    /// <summary>The child receives an independent snapshot of the parent's namespace.</summary>
    Copy,

    /// <summary>The child receives a new empty namespace.</summary>
    Empty,
}

/// <summary>A virtual Plan 9 process group which owns a namespace mount table.</summary>
public sealed class VProcessGroup
{
    private readonly object gate = new();
    private int owners;

    /// <summary>Creates a virtual process group.</summary>
    public VProcessGroup(long id, MountTable? mountTable = null)
    {
        if (id <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        Id = id;
        MountTable = mountTable ?? new MountTable();
    }

    /// <summary>Gets the process-group identity.</summary>
    public long Id { get; }

    /// <summary>Gets the namespace owned by this process group.</summary>
    public MountTable MountTable { get; }

    /// <summary>Gets the number of virtual processes owning this group.</summary>
    public int OwnerCount
    {
        get
        {
            lock (gate)
            {
                return owners;
            }
        }
    }

    internal void Retain()
    {
        lock (gate)
        {
            MountTable.EnsureOpen();
            owners++;
        }
    }

    internal void Release()
    {
        lock (gate)
        {
            if (--owners == 0)
                MountTable.Close();
        }
    }
}

/// <summary>A virtual process with root, current directory, and process-group identity.</summary>
public sealed class VProcess
{
    private readonly object gate = new();
    private VProcessGroup processGroup;
    private DescriptorGroup descriptorGroup;
    private readonly TaskCompletionSource terminationCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private NamespaceChannel? root;
    private NamespaceChannel? currentDirectory;

    /// <summary>Creates a virtual process.</summary>
    public VProcess(
        long id,
        long? parentId,
        VProcessGroup processGroup,
        NamespaceChannel root,
        NamespaceChannel currentDirectory,
        DescriptorGroup? descriptorGroup = null)
    {
        if (id <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        Id = id;
        ParentId = parentId;
        this.processGroup = processGroup ?? throw new ArgumentNullException(nameof(processGroup));
        this.root = root?.Clone() ?? throw new ArgumentNullException(nameof(root));
        this.currentDirectory = currentDirectory?.Clone() ?? throw new ArgumentNullException(nameof(currentDirectory));
        this.descriptorGroup = descriptorGroup ?? new DescriptorGroup();
        processGroup.Retain();
        try
        {
            this.descriptorGroup.RetainOwner();
        }
        catch
        {
            processGroup.Release();
            throw;
        }
    }

    /// <summary>Gets the virtual process identity.</summary>
    public long Id { get; }

    /// <summary>Gets the parent virtual process identity.</summary>
    public long? ParentId { get; }

    /// <summary>Gets the independently shared process descriptor table.</summary>
    public DescriptorGroup Descriptors
    {
        get { lock (gate) { EnsureAlive(); return descriptorGroup; } }
    }

    internal DescriptorLease AcquireDescriptor(int descriptor)
        => AcquireDescriptor(descriptor, out _);

    internal DescriptorLease AcquireDescriptor(int descriptor, out MountTable mounts)
    {
        lock (gate)
        {
            EnsureAlive();
            mounts = processGroup.MountTable;
            return descriptorGroup.Acquire(descriptor);
        }
    }

    internal int InstallDescriptor(ResourceOpenHandle handle, Func<ValueTask> close, bool closeOnExec,
        IDirectoryCursor? directory = null, string? visibleName = null, bool isMountPoint = false)
    {
        lock (gate)
        {
            EnsureAlive();
            return descriptorGroup.InstallWithCursor(handle, close, closeOnExec, directory, visibleName, isMountPoint);
        }
    }

    internal (NamespaceChannel Channel, MountTable Mounts) CapturePath(bool rooted)
    {
        lock (gate)
        {
            EnsureAlive();
            return ((rooted ? root : currentDirectory)!.Clone(), processGroup.MountTable);
        }
    }

    /// <summary>
    /// Completes after termination releases this process's descriptor ownership.
    /// Other processes and admitted I/O can still retain their own references.
    /// </summary>
    public Task TerminationCompletion => terminationCompletion.Task;

    internal Task RforkDescriptorsAsync(DescriptorForkMode mode)
    {
        DescriptorGroup previous;
        lock (gate)
        {
            EnsureAlive();
            DescriptorGroup replacement = mode switch
            {
                DescriptorForkMode.Share => descriptorGroup,
                DescriptorForkMode.Copy => descriptorGroup.Copy(),
                DescriptorForkMode.Empty => new DescriptorGroup(),
                _ => throw new ArgumentOutOfRangeException(nameof(mode)),
            };
            replacement.RetainOwner();
            previous = descriptorGroup;
            descriptorGroup = replacement;
        }

        return previous.ReleaseOwnerAsync();
    }

    /// <summary>Gets the process group controlling this process's namespace.</summary>
    public VProcessGroup ProcessGroup
    {
        get
        {
            lock (gate)
            {
                EnsureAlive();
                return processGroup;
            }
        }
    }

    /// <summary>Gets the process's namespace root channel.</summary>
    public NamespaceChannel Root
    {
        get
        {
            lock (gate)
            {
                EnsureAlive();
                return root!;
            }
        }
    }

    /// <summary>Gets the process's current-directory channel.</summary>
    public NamespaceChannel CurrentDirectory
    {
        get
        {
            lock (gate)
            {
                EnsureAlive();
                return currentDirectory!;
            }
        }
    }

    /// <summary>Gets whether this process has released its channels and namespace ownership.</summary>
    public bool IsTerminated
    {
        get
        {
            lock (gate)
            {
                return root is null;
            }
        }
    }

    /// <summary>Replaces the namespace group associated with this process.</summary>
    public void ReplaceProcessGroup(VProcessGroup processGroup)
    {
        ArgumentNullException.ThrowIfNull(processGroup);
        lock (gate)
        {
            EnsureAlive();
            // Retain first, including replacement by the same group.
            processGroup.Retain();
            this.processGroup.Release();
            this.processGroup = processGroup;
        }
    }

    /// <summary>Changes the current directory without changing the namespace root.</summary>
    public void ChangeDirectory(NamespaceChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        lock (gate)
        {
            EnsureAlive();
            currentDirectory = channel.Clone();
        }
    }

    internal void Terminate()
    {
        lock (gate)
        {
            // VProcessTable removes this process before releasing its ownership.
            root = null;
            currentDirectory = null;
            processGroup.Release();
        }

        _ = FinishTerminationAsync();
    }

    private async Task FinishTerminationAsync()
    {
        await descriptorGroup.ReleaseOwnerAsync();
        terminationCompletion.SetResult();
    }

    private void EnsureAlive()
    {
        if (root is null)
            throw new NamespaceException(NamespaceError.NamespaceClosed, "The virtual process has terminated.");
    }
}

/// <summary>Creates and forks virtual processes while preserving Plan 9 namespace-group semantics.</summary>
public sealed class VProcessTable
{
    private readonly object gate = new();
    private readonly Dictionary<long, VProcess> processes = new();
    private long nextProcessId;
    private long nextProcessGroupId;

    /// <summary>Creates the initial process and process group.</summary>
    public VProcess CreateInitial(NamespaceChannel root)
    {
        ArgumentNullException.ThrowIfNull(root);
        lock (gate)
        {
            var group = new VProcessGroup(++nextProcessGroupId);
            var process = new VProcess(++nextProcessId, null, group, root, root);
            processes.Add(process.Id, process);
            return process;
        }
    }

    /// <summary>Forks a child with a shared, copied, or empty namespace group.</summary>
    public VProcess Fork(
        long parentId,
        NamespaceForkMode mode,
        bool noMounts = false,
        DescriptorForkMode descriptorMode = DescriptorForkMode.Share)
    {
        lock (gate)
        {
            if (!processes.TryGetValue(parentId, out VProcess? parent))
            {
                throw new KeyNotFoundException($"Virtual process {parentId} does not exist.");
            }

            if (descriptorMode is < DescriptorForkMode.Share or > DescriptorForkMode.Empty)
                throw new ArgumentOutOfRangeException(nameof(descriptorMode));

            VProcessGroup group = mode switch
            {
                NamespaceForkMode.Share => parent.ProcessGroup,
                NamespaceForkMode.Copy => new VProcessGroup(
                    ++nextProcessGroupId,
                    parent.ProcessGroup.MountTable.Clone()),
                NamespaceForkMode.Empty => new VProcessGroup(++nextProcessGroupId),
                _ => throw new ArgumentOutOfRangeException(nameof(mode)),
            };

            var child = new VProcess(
                ++nextProcessId,
                parent.Id,
                group,
                parent.Root,
                parent.CurrentDirectory,
                descriptorMode switch
                {
                    DescriptorForkMode.Share => parent.Descriptors,
                    DescriptorForkMode.Copy => parent.Descriptors.Copy(),
                    _ => new DescriptorGroup(),
                });
            if (noMounts)
            {
                group.MountTable.SetMountsDisabled(true);
            }
            processes.Add(child.Id, child);
            return child;
        }
    }

    /// <summary>Applies namespace rfork semantics without creating a child process.</summary>
    public VProcess RforkNamespace(long processId, NamespaceForkMode mode, bool noMounts = false)
    {
        lock (gate)
        {
            if (!processes.TryGetValue(processId, out VProcess? process))
            {
                throw new KeyNotFoundException($"Virtual process {processId} does not exist.");
            }

            VProcessGroup group = mode switch
            {
                NamespaceForkMode.Share => process.ProcessGroup,
                NamespaceForkMode.Copy => new VProcessGroup(
                    ++nextProcessGroupId,
                    process.ProcessGroup.MountTable.Clone()),
                NamespaceForkMode.Empty => new VProcessGroup(++nextProcessGroupId),
                _ => throw new ArgumentOutOfRangeException(nameof(mode)),
            };
            if (noMounts)
            {
                group.MountTable.SetMountsDisabled(true);
            }

            process.ReplaceProcessGroup(group);
            return process;
        }
    }

    /// <summary>Terminates a process, releasing its channels and namespace ownership.</summary>
    /// <returns>True if a process was removed; false if the identity was already absent.</returns>
    public bool Terminate(long processId)
    {
        lock (gate)
        {
            if (!processes.Remove(processId, out VProcess? process))
                return false;
            process.Terminate();
            return true;
        }
    }

    /// <summary>Changes descriptor inheritance without creating a child or changing namespace ownership.</summary>
    public Task RforkDescriptorsAsync(long processId, DescriptorForkMode mode)
    {
        lock (gate)
            return Get(processId).RforkDescriptorsAsync(mode);
    }

    /// <summary>Terminates a process and awaits release of its descriptor ownership.</summary>
    public async Task<bool> TerminateAsync(long processId)
    {
        VProcess process;
        lock (gate)
        {
            if (!processes.Remove(processId, out process!)) return false;
            process.Terminate();
        }

        await process.TerminationCompletion;
        return true;
    }

    /// <summary>Finds a virtual process by identity.</summary>
    public VProcess Get(long processId)
    {
        lock (gate)
        {
            return processes.TryGetValue(processId, out VProcess? process)
                ? process
                : throw new KeyNotFoundException($"Virtual process {processId} does not exist.");
        }
    }

    /// <summary>Returns a stable snapshot of all virtual processes.</summary>
    public IReadOnlyList<VProcess> Snapshot()
    {
        lock (gate)
        {
            return processes.Values.OrderBy(process => process.Id).ToArray();
        }
    }
}
