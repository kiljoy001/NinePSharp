namespace NinePSharp.Namespaces;

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
            {
                throw new ArgumentOutOfRangeException(nameof(descriptorMode));
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

            DescriptorGroup descriptors = descriptorMode switch
            {
                DescriptorForkMode.Share => parent.Descriptors,
                DescriptorForkMode.Copy => parent.Descriptors.Copy(),
                _ => new DescriptorGroup(),
            };
            var child = new VProcess(
                ++nextProcessId,
                parent.Id,
                group,
                parent.Root,
                parent.CurrentDirectory,
                descriptors);
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
            {
                return false;
            }

            process.Terminate();
            return true;
        }
    }

    /// <summary>Changes descriptor inheritance without creating a child or changing namespace ownership.</summary>
    public Task RforkDescriptorsAsync(long processId, DescriptorForkMode mode)
    {
        lock (gate)
        {
            return Get(processId).RforkDescriptorsAsync(mode);
        }
    }

    /// <summary>Terminates a process and awaits release of its descriptor ownership.</summary>
    public async Task<bool> TerminateAsync(long processId)
    {
        VProcess process;
        lock (gate)
        {
            if (!processes.Remove(processId, out process!))
            {
                return false;
            }

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
