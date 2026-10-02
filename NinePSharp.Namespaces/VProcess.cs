namespace NinePSharp.Namespaces;

/// <summary>A virtual process with root, current directory, and process-group identity.</summary>
public sealed class VProcess
{
    private readonly object gate = new();
    private readonly TaskCompletionSource terminationCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private VProcessGroup processGroup;
    private DescriptorGroup descriptorGroup;
    private NamespaceChannel? root;
    private NamespaceChannel? currentDirectory;

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
        get
        {
            lock (gate)
{
    EnsureAlive();
    return descriptorGroup;
}
        }
    }

    /// <summary>
    /// Gets completes after termination releases this process's descriptor ownership.
    /// Other processes and admitted I/O can still retain their own references.
    /// </summary>
    public Task TerminationCompletion => terminationCompletion.Task;

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

    /// <summary>Gets a value indicating whether this process has released its channels and namespace ownership.</summary>
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

    internal int InstallDescriptor(
        ResourceOpenHandle handle,
        Func<ValueTask> close,
        bool closeOnExec,
        IDirectoryCursor? directory = null,
        string? visibleName = null,
        bool isMountPoint = false)
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
        {
            throw new NamespaceException(NamespaceError.NamespaceClosed, "The virtual process has terminated.");
        }
    }
}
