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
}

/// <summary>A virtual process with root, current directory, and process-group identity.</summary>
public sealed class VProcess
{
    /// <summary>Creates a virtual process.</summary>
    public VProcess(
        long id,
        long? parentId,
        VProcessGroup processGroup,
        NamespaceChannel root,
        NamespaceChannel currentDirectory)
    {
        if (id <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        Id = id;
        ParentId = parentId;
        ProcessGroup = processGroup ?? throw new ArgumentNullException(nameof(processGroup));
        Root = root?.Clone() ?? throw new ArgumentNullException(nameof(root));
        CurrentDirectory = currentDirectory?.Clone() ?? throw new ArgumentNullException(nameof(currentDirectory));
    }

    /// <summary>Gets the virtual process identity.</summary>
    public long Id { get; }

    /// <summary>Gets the parent virtual process identity.</summary>
    public long? ParentId { get; }

    /// <summary>Gets the process group controlling this process's namespace.</summary>
    public VProcessGroup ProcessGroup { get; }

    /// <summary>Gets the process's namespace root channel.</summary>
    public NamespaceChannel Root { get; }

    /// <summary>Gets the process's current-directory channel.</summary>
    public NamespaceChannel CurrentDirectory { get; private set; }

    /// <summary>Changes the current directory without changing the namespace root.</summary>
    public void ChangeDirectory(NamespaceChannel channel)
        => CurrentDirectory = channel?.Clone() ?? throw new ArgumentNullException(nameof(channel));
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
    public VProcess Fork(long parentId, NamespaceForkMode mode)
    {
        lock (gate)
        {
            if (!processes.TryGetValue(parentId, out VProcess? parent))
            {
                throw new KeyNotFoundException($"Virtual process {parentId} does not exist.");
            }

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
                parent.CurrentDirectory);
            processes.Add(child.Id, child);
            return child;
        }
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
}
