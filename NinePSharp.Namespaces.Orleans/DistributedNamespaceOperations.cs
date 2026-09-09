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
}
