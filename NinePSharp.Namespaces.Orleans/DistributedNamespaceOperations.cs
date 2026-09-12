namespace NinePSharp.Namespaces.Orleans;

/// <summary>
/// Applies durable process-group mount state while routing data operations to resource grains.
/// </summary>
public sealed class DistributedNamespaceOperations
{
    private readonly IGrainFactory grainFactory;
    private readonly IResourceOperations resources;

    /// <summary>Initializes a distributed namespace data plane.</summary>
    public DistributedNamespaceOperations(IGrainFactory grainFactory, IResourceOperations resources)
    {
        this.grainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
        this.resources = resources ?? throw new ArgumentNullException(nameof(resources));
    }

    /// <summary>Attaches a resource root and crosses a mount at that identity.</summary>
    public async Task<NamespaceChannel> AttachAsync(
        string processGroupId,
        ResourceHandle root,
        CancellationToken cancellationToken = default)
    {
        NamespaceNavigator navigator = await CreateNavigatorAsync(processGroupId, cancellationToken);
        return navigator.Attach(root);
    }

    /// <summary>Walks names using one durable process group's current mount snapshot.</summary>
    public async Task<NamespaceWalkResult> WalkAsync(
        string processGroupId,
        NamespaceChannel source,
        IReadOnlyList<string> names,
        CancellationToken cancellationToken = default)
    {
        NamespaceNavigator navigator = await CreateNavigatorAsync(processGroupId, cancellationToken);
        return await navigator.WalkAsync(source, names, cancellationToken);
    }

    /// <summary>Reads a directory using ordered union semantics.</summary>
    public async Task<IReadOnlyList<ResourceDirectoryEntry>> ReadDirectoryAsync(
        string processGroupId,
        NamespaceChannel channel,
        CancellationToken cancellationToken = default)
    {
        NamespaceNavigator navigator = await CreateNavigatorAsync(processGroupId, cancellationToken);
        return await navigator.ReadDirectoryAsync(channel, cancellationToken);
    }

    /// <summary>Reads stat metadata for the concatenated visible directory.</summary>
    public async Task<IReadOnlyList<ResourceStat>> ReadDirectoryStatsAsync(
        string processGroupId,
        NamespaceChannel channel,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ResourceDirectoryEntry> entries = await ReadDirectoryAsync(
            processGroupId,
            channel,
            cancellationToken);
        IResourceDataOperations data = RequireDataOperations();
        var result = new List<ResourceStat>(entries.Count);
        foreach (ResourceDirectoryEntry entry in entries)
        {
            ResourceStat stat = await data.StatAsync(entry.Handle, cancellationToken);
            result.Add(stat with { Name = entry.Name });
        }

        return result;
    }

    /// <summary>Creates through the first mounted member marked for creation.</summary>
    public async Task<ResourceHandle> CreateAsync(
        string processGroupId,
        NamespaceChannel channel,
        string name,
        bool directory,
        CancellationToken cancellationToken = default)
    {
        NamespaceNavigator navigator = await CreateNavigatorAsync(processGroupId, cancellationToken);
        return await navigator.CreateAsync(channel, name, directory, cancellationToken);
    }

    /// <summary>Opens the resource selected by a namespace channel.</summary>
    public async Task<ResourceOpenHandle> OpenAsync(
        NamespaceChannel channel,
        byte mode,
        ResourceOperationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return await RequireDataOperations().OpenAsync(channel.Current, mode, context, cancellationToken);
    }

    /// <summary>Reads bytes from an open resource without consulting the mount-table grain.</summary>
    public async Task<ReadOnlyMemory<byte>> ReadAsync(
        ResourceOpenHandle openHandle,
        ulong offset,
        uint count,
        CancellationToken cancellationToken = default)
        => await RequireDataOperations().ReadAsync(openHandle, offset, count, cancellationToken);

    /// <summary>Writes bytes to an open resource without consulting the mount-table grain.</summary>
    public async Task<uint> WriteAsync(
        ResourceOpenHandle openHandle,
        ulong offset,
        ReadOnlyMemory<byte> data,
        ResourceOperationContext context,
        CancellationToken cancellationToken = default)
        => await RequireDataOperations().WriteAsync(openHandle, offset, data, context, cancellationToken);

    /// <summary>Reads metadata while preserving the name visible through the mount.</summary>
    public async Task<ResourceStat> StatAsync(
        string processGroupId,
        NamespaceChannel channel,
        CancellationToken cancellationToken = default)
    {
        _ = await CreateNavigatorAsync(processGroupId, cancellationToken);
        ResourceStat result = await RequireDataOperations().StatAsync(channel.Current, cancellationToken);
        string visibleName = channel.Frames.Count == 1 ? "/" : channel.Frames[^1].Name;
        return result with { Name = visibleName };
    }

    /// <summary>Creates and opens through the current MCREATE union member.</summary>
    public async Task<NamespaceCreateResult> CreateAndOpenAsync(
        string processGroupId,
        NamespaceChannel channel,
        string name,
        uint permissions,
        byte mode,
        ResourceOperationContext context,
        CancellationToken cancellationToken = default)
    {
        NamespaceNavigator navigator = await CreateNavigatorAsync(processGroupId, cancellationToken);
        ResourceHandle target = navigator.SelectCreateTarget(channel);
        ResourceOpenHandle opened = await RequireDataOperations().CreateAndOpenAsync(
            target,
            name,
            permissions,
            mode,
            context,
            cancellationToken);
        return new NamespaceCreateResult(navigator.EnterCreated(channel, name, opened.Resource), opened);
    }

    /// <summary>Closes provider-owned state without consulting the mount-table grain.</summary>
    public async Task ClunkAsync(
        ResourceOpenHandle openHandle,
        ResourceOperationContext context,
        CancellationToken cancellationToken = default)
        => await RequireDataOperations().ClunkAsync(openHandle, context, cancellationToken);

    /// <summary>Removes the resource selected by a channel.</summary>
    public async Task RemoveAsync(
        NamespaceChannel channel,
        ResourceOpenHandle? openHandle,
        ResourceOperationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        await RequireDataOperations().RemoveAsync(channel.Current, openHandle, context, cancellationToken);
    }

    /// <summary>Binds a channel into the durable process-group namespace.</summary>
    public async Task<MountBinding> BindAsync(
        string processGroupId,
        NamespaceChannel source,
        ResourceHandle mountedOn,
        MountFlags flags = MountFlags.Replace,
        string? spec = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processGroupId);
        cancellationToken.ThrowIfCancellationRequested();
        IVProcessGroupGrain group = grainFactory.GetGrain<IVProcessGroupGrain>(processGroupId);
        MountBindingModel result = await group.BindAsync(source.ToModel(), mountedOn.ToModel(), flags, spec)
            .WaitAsync(cancellationToken);
        return result.ToDomain();
    }

    /// <summary>Mounts a service resource into the durable process-group namespace.</summary>
    public async Task<MountBinding> MountAsync(
        string processGroupId,
        ResourceHandle target,
        ResourceHandle mountedOn,
        MountFlags flags = MountFlags.Replace,
        string? spec = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processGroupId);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(mountedOn);
        cancellationToken.ThrowIfCancellationRequested();
        IVProcessGroupGrain group = grainFactory.GetGrain<IVProcessGroupGrain>(processGroupId);
        MountBindingModel result = await group.MountAsync(
                target.ToModel(),
                mountedOn.ToModel(),
                flags,
                spec)
            .WaitAsync(cancellationToken);
        return result.ToDomain();
    }

    /// <summary>Removes every mount or one selected member from a mount point.</summary>
    public async Task UnmountAsync(
        string processGroupId,
        ResourceHandle mountedOn,
        ResourceHandle? mounted = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processGroupId);
        ArgumentNullException.ThrowIfNull(mountedOn);
        cancellationToken.ThrowIfCancellationRequested();
        IVProcessGroupGrain group = grainFactory.GetGrain<IVProcessGroupGrain>(processGroupId);
        await group.UnmountAsync(mountedOn.ToModel(), mounted?.ToModel()).WaitAsync(cancellationToken);
    }

    /// <summary>Enables or disables every service mount in a process namespace.</summary>
    public async Task SetMountsDisabledAsync(
        string processGroupId,
        bool disabled,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processGroupId);
        cancellationToken.ThrowIfCancellationRequested();
        IVProcessGroupGrain group = grainFactory.GetGrain<IVProcessGroupGrain>(processGroupId);
        await group.SetMountsDisabledAsync(disabled).WaitAsync(cancellationToken);
    }

    /// <summary>Blocks or permits service mounts targeting one device name.</summary>
    public async Task SetMountDeviceBlockedAsync(
        string processGroupId,
        string device,
        bool blocked,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processGroupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(device);
        cancellationToken.ThrowIfCancellationRequested();
        IVProcessGroupGrain group = grainFactory.GetGrain<IVProcessGroupGrain>(processGroupId);
        await group.SetMountDeviceBlockedAsync(device, blocked).WaitAsync(cancellationToken);
    }

    private async Task<NamespaceNavigator> CreateNavigatorAsync(
        string processGroupId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processGroupId);
        cancellationToken.ThrowIfCancellationRequested();
        IVProcessGroupGrain group = grainFactory.GetGrain<IVProcessGroupGrain>(processGroupId);
        NamespaceSnapshotModel snapshot = await group.GetSnapshotAsync().WaitAsync(cancellationToken);
        return new NamespaceNavigator(MountTable.FromSnapshot(snapshot.ToDomain()), resources);
    }

    private IResourceDataOperations RequireDataOperations()
        => resources as IResourceDataOperations
            ?? throw new NotSupportedException("The resource provider does not implement data operations.");
}
