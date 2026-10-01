using Orleans.Runtime;

namespace NinePSharp.Namespaces.Orleans;

/// <summary>Durably journals wstat admission and terminal provider outcomes for one session.</summary>
public sealed class WStatRecoveryJournalGrain : Grain, IWStatRecoveryJournalGrain
{
    private readonly IPersistentState<WStatRecoveryPersistentState> state;
    private readonly string? sessionId;

    /// <summary>Initializes a new instance of the <see cref="WStatRecoveryJournalGrain"/> class.</summary>
    public WStatRecoveryJournalGrain(
        [PersistentState("wstat-recovery")] IPersistentState<WStatRecoveryPersistentState> state)
    {
        this.state = state;
    }

    internal WStatRecoveryJournalGrain(
        IPersistentState<WStatRecoveryPersistentState> state,
        string sessionId)
        : this(state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        this.sessionId = sessionId;
    }

    /// <inheritdoc/>
    public async Task<WStatRecoveryRecordModel> BeginAsync(WStatRecoveryRequestModel request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ResourceOperationIdModel operationId = request.Context.OperationId;
        if (!string.Equals(operationId.SessionId, SessionId, StringComparison.Ordinal))
            throw new InvalidOperationException("The wstat operation belongs to a different recovery session.");
        if (state.State.Records.TryGetValue(operationId.Sequence, out WStatRecoveryRecordModel? existing))
        {
            EnsureFingerprint(existing, request.Fingerprint);
            return existing;
        }

        var created = new WStatRecoveryRecordModel(request, WStatRecoveryStateModel.Pending, null, null);
        state.State.Records.Add(operationId.Sequence, created);
        await state.WriteStateAsync();
        return created;
    }

    /// <inheritdoc/>
    public Task<WStatRecoveryRecordModel?> GetAsync(ulong sequence)
    {
        state.State.Records.TryGetValue(sequence, out WStatRecoveryRecordModel? record);
        return Task.FromResult(record);
    }

    /// <inheritdoc/>
    public Task<WStatRecoveryRecordModel[]> GetPendingAsync()
        => Task.FromResult(state.State.Records
            .OrderBy(pair => pair.Key)
            .Select(pair => pair.Value)
            .Where(record => record.State == WStatRecoveryStateModel.Pending)
            .ToArray());

    /// <inheritdoc/>
    public async Task CommitAsync(ulong sequence, string fingerprint, uint result)
    {
        WStatRecoveryRecordModel record = Require(sequence, fingerprint);
        if (record.State == WStatRecoveryStateModel.Rejected)
            throw new InvalidOperationException("A rejected wstat operation cannot be committed.");
        if (record.State == WStatRecoveryStateModel.Committed)
        {
            if (record.Result != result)
                throw new InvalidOperationException("A committed wstat operation cannot change its result.");
            return;
        }

        state.State.Records[sequence] = record with
        {
            State = WStatRecoveryStateModel.Committed,
            Result = result,
            Error = null,
        };
        await state.WriteStateAsync();
    }

    /// <inheritdoc/>
    public async Task RejectAsync(ulong sequence, string fingerprint, string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        WStatRecoveryRecordModel record = Require(sequence, fingerprint);
        if (record.State == WStatRecoveryStateModel.Committed)
            throw new InvalidOperationException("A committed wstat operation cannot be rejected.");
        if (record.State == WStatRecoveryStateModel.Rejected)
        {
            if (!string.Equals(record.Error, error, StringComparison.Ordinal))
                throw new InvalidOperationException("A rejected wstat operation cannot change its error.");
            return;
        }

        state.State.Records[sequence] = record with
        {
            State = WStatRecoveryStateModel.Rejected,
            Result = null,
            Error = error,
        };
        await state.WriteStateAsync();
    }

    private WStatRecoveryRecordModel Require(ulong sequence, string fingerprint)
    {
        if (!state.State.Records.TryGetValue(sequence, out WStatRecoveryRecordModel? record))
            throw new KeyNotFoundException("The wstat operation was not durably admitted.");
        EnsureFingerprint(record, fingerprint);
        return record;
    }

    private static void EnsureFingerprint(WStatRecoveryRecordModel record, string fingerprint)
    {
        if (!string.Equals(record.Request.Fingerprint, fingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("An operation identity was reused for a different wstat request.");
    }

    private string SessionId => sessionId ?? this.GetPrimaryKeyString();
}
