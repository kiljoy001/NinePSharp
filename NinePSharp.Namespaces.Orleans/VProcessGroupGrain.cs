using Orleans.Runtime;

namespace NinePSharp.Namespaces.Orleans;

/// <summary>Persists and serializes mutations to one virtual process group's mount table.</summary>
public sealed class VProcessGroupGrain : Grain, IVProcessGroupGrain
{
    private readonly IPersistentState<VProcessGroupPersistentState> state;

    /// <summary>Initializes a new instance of the <see cref="VProcessGroupGrain"/> class.</summary>
    public VProcessGroupGrain(
        [PersistentState("namespace")] IPersistentState<VProcessGroupPersistentState> state)
    {
        this.state = state;
    }

    /// <inheritdoc/>
    public Task InitializeEmptyAsync()
        => InitializeFromAsync(new NamespaceSnapshot(0, Array.Empty<MountHead>()).ToModel());

    /// <inheritdoc/>
    public async Task InitializeFromAsync(NamespaceSnapshotModel snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (state.State.Initialized)
        {
            if (state.State.Namespace is null || !state.State.Namespace.EquivalentTo(snapshot))
            {
                throw new InvalidOperationException("The virtual process group is already initialized.");
            }

            return;
        }

        _ = MountTable.FromSnapshot(snapshot.ToDomain());
        VProcessGroupPersistentState previous = state.State;
        state.State = new VProcessGroupPersistentState { Initialized = true, Namespace = snapshot };
        try
        {
            await state.WriteStateAsync();
        }
        catch
        {
            state.State = previous;
            throw;
        }
    }

    /// <inheritdoc/>
    public Task<NamespaceSnapshotModel> GetSnapshotAsync()
        => Task.FromResult(RequireSnapshot());

    /// <inheritdoc/>
    public async Task<MountBindingModel> MountAsync(
        ResourceHandleModel target,
        ResourceHandleModel mountedOn,
        MountFlags flags,
        string? spec = null)
    {
        MountTable table = LoadTable();
        MountBinding result = table.Mount(target.ToDomain(), mountedOn.ToDomain(), flags, spec);
        await SaveAsync(table);
        return result.ToModel();
    }

    /// <inheritdoc/>
    public async Task<MountBindingModel> BindAsync(
        NamespaceChannelModel source,
        ResourceHandleModel mountedOn,
        MountFlags flags,
        string? spec = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        MountTable table = LoadTable();
        MountBinding result = table.Mount(source.ToDomain(), mountedOn.ToDomain(), flags, spec);
        await SaveAsync(table);
        return result.ToModel();
    }

    /// <inheritdoc/>
    public async Task UnmountAsync(ResourceHandleModel mountedOn, ResourceHandleModel? mounted = null)
    {
        MountTable table = LoadTable();
        table.Unmount(mountedOn.ToDomain(), mounted?.ToDomain());
        await SaveAsync(table);
    }

    /// <inheritdoc/>
    public async Task SetMountsDisabledAsync(bool disabled)
    {
        MountTable table = LoadTable();
        table.SetMountsDisabled(disabled);
        await SaveAsync(table);
    }

    /// <inheritdoc/>
    public async Task SetMountDeviceBlockedAsync(string device, bool blocked)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(device);
        MountTable table = LoadTable();
        table.SetMountDeviceBlocked(device, blocked);
        await SaveAsync(table);
    }

    /// <inheritdoc/>
    public Task<MountHeadModel?> FindMountAsync(ResourceIdentityModel identity)
    {
        MountHead? head = LoadTable().Find(identity.ToDomain());
        MountHeadModel? result = head is null
            ? null
            : new MountHeadModel(head.From.ToModel(), head.Mounts.Select(NamespaceModelConversions.ToModel).ToArray());
        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public async Task CloneToAsync(string destinationProcessGroupId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationProcessGroupId);
        NamespaceSnapshotModel clone = LoadTable().Clone().Snapshot().ToModel();
        IVProcessGroupGrain destination = GrainFactory.GetGrain<IVProcessGroupGrain>(destinationProcessGroupId);
        await destination.InitializeFromAsync(clone);
    }

    private MountTable LoadTable() => MountTable.FromSnapshot(RequireSnapshot().ToDomain());

    private NamespaceSnapshotModel RequireSnapshot()
        => state.State.Initialized && state.State.Namespace is not null
            ? state.State.Namespace
            : throw new InvalidOperationException("The virtual process group has not been initialized.");

    private async Task SaveAsync(MountTable table)
    {
        VProcessGroupPersistentState previous = state.State;
        state.State = new VProcessGroupPersistentState
        {
            Initialized = true,
            Namespace = table.Snapshot().ToModel(),
        };

        try
        {
            await state.WriteStateAsync();
        }
        catch
        {
            state.State = previous;
            throw;
        }
    }
}
