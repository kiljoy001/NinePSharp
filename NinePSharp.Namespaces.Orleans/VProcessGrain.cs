using Orleans.Runtime;

namespace NinePSharp.Namespaces.Orleans;

/// <summary>Persists virtual process identity, channels, and namespace-group ownership.</summary>
public sealed class VProcessGrain : Grain, IVProcessGrain
{
    private readonly IPersistentState<VProcessPersistentState> state;

    /// <summary>Initializes a new instance of the <see cref="VProcessGrain"/> class.</summary>
    public VProcessGrain([PersistentState("process")] IPersistentState<VProcessPersistentState> state)
    {
        this.state = state;
    }

    /// <inheritdoc/>
    public async Task InitializeAsync(VProcessStateModel initialState)
    {
        ArgumentNullException.ThrowIfNull(initialState);
        if (initialState.ProcessId != this.GetPrimaryKeyLong())
        {
            throw new ArgumentException("The state process ID must equal the grain key.", nameof(initialState));
        }

        Validate(initialState);
        if (state.State.Initialized)
        {
            if (state.State.Process is null || !state.State.Process.EquivalentTo(initialState))
            {
                throw new InvalidOperationException("The virtual process is already initialized.");
            }

            return;
        }

        VProcessPersistentState previous = state.State;
        state.State = new VProcessPersistentState { Initialized = true, Process = initialState };
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
    public Task<VProcessStateModel> GetStateAsync() => Task.FromResult(RequireProcess());

    /// <inheritdoc/>
    public async Task ChangeDirectoryAsync(NamespaceChannelModel currentDirectory)
    {
        ArgumentNullException.ThrowIfNull(currentDirectory);
        _ = currentDirectory.ToDomain();
        VProcessStateModel current = RequireProcess();
        await SaveAsync(current with { CurrentDirectory = currentDirectory });
    }

    /// <inheritdoc/>
    public async Task<VProcessStateModel> ForkAsync(
        long childProcessId,
        NamespaceForkModeModel mode,
        bool noMounts = false)
    {
        if (childProcessId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(childProcessId));
        }

        VProcessStateModel parent = RequireProcess();
        string childGroupId = mode switch
        {
            NamespaceForkModeModel.Share => parent.ProcessGroupId,
            NamespaceForkModeModel.Copy => $"vprocess-{childProcessId}-copy",
            NamespaceForkModeModel.Empty => $"vprocess-{childProcessId}-empty",
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        if (mode == NamespaceForkModeModel.Copy)
        {
            IVProcessGroupGrain parentGroup = GrainFactory.GetGrain<IVProcessGroupGrain>(parent.ProcessGroupId);
            await parentGroup.CloneToAsync(childGroupId);
        }
        else if (mode == NamespaceForkModeModel.Empty)
        {
            IVProcessGroupGrain childGroup = GrainFactory.GetGrain<IVProcessGroupGrain>(childGroupId);
            await childGroup.InitializeEmptyAsync();
        }

        if (noMounts)
        {
            IVProcessGroupGrain childGroup = GrainFactory.GetGrain<IVProcessGroupGrain>(childGroupId);
            await childGroup.SetMountsDisabledAsync(true);
        }

        var childState = new VProcessStateModel(
            childProcessId,
            parent.ProcessId,
            childGroupId,
            parent.Root,
            parent.CurrentDirectory);
        IVProcessGrain child = GrainFactory.GetGrain<IVProcessGrain>(childProcessId);
        await child.InitializeAsync(childState);
        return childState;
    }

    /// <inheritdoc/>
    public async Task<VProcessStateModel> RforkNamespaceAsync(
        NamespaceForkModeModel mode,
        bool noMounts = false)
    {
        VProcessStateModel current = RequireProcess();
        string groupId = mode switch
        {
            NamespaceForkModeModel.Share => current.ProcessGroupId,
            NamespaceForkModeModel.Copy => $"vprocess-{current.ProcessId}-rfork-copy",
            NamespaceForkModeModel.Empty => $"vprocess-{current.ProcessId}-rfork-empty",
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        if (mode == NamespaceForkModeModel.Copy)
        {
            IVProcessGroupGrain parentGroup = GrainFactory.GetGrain<IVProcessGroupGrain>(current.ProcessGroupId);
            await parentGroup.CloneToAsync(groupId);
        }
        else if (mode == NamespaceForkModeModel.Empty)
        {
            IVProcessGroupGrain group = GrainFactory.GetGrain<IVProcessGroupGrain>(groupId);
            await group.InitializeEmptyAsync();
        }

        if (noMounts)
        {
            IVProcessGroupGrain group = GrainFactory.GetGrain<IVProcessGroupGrain>(groupId);
            await group.SetMountsDisabledAsync(true);
        }

        VProcessStateModel updated = current with { ProcessGroupId = groupId };
        await SaveAsync(updated);
        return updated;
    }

    private static void Validate(VProcessStateModel value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value.ProcessGroupId);
        _ = value.Root.ToDomain();
        _ = value.CurrentDirectory.ToDomain();
    }

    private VProcessStateModel RequireProcess()
        => state.State.Initialized && state.State.Process is not null
            ? state.State.Process
            : throw new InvalidOperationException("The virtual process has not been initialized.");

    private async Task SaveAsync(VProcessStateModel process)
    {
        VProcessPersistentState previous = state.State;
        state.State = new VProcessPersistentState { Initialized = true, Process = process };
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
