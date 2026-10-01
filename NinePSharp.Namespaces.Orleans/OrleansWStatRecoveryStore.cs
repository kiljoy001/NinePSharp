namespace NinePSharp.Namespaces.Orleans;

/// <summary>Maps the recovery-store contract to one durable Orleans grain per session epoch.</summary>
public sealed class OrleansWStatRecoveryStore : IWStatRecoveryStore
{
    private readonly IGrainFactory grainFactory;

    /// <summary>Initializes a new instance of the <see cref="OrleansWStatRecoveryStore"/> class.</summary>
    public OrleansWStatRecoveryStore(IGrainFactory grainFactory)
    {
        this.grainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
    }

    /// <inheritdoc/>
    public async ValueTask<WStatRecoveryRecord> BeginAsync(
        WStatRecoveryRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WStatRecoveryRecordModel result = await Grain(request.Context.OperationId.SessionId)
            .BeginAsync(request.ToModel())
            .WaitAsync(cancellationToken);
        return result.ToDomain();
    }

    /// <inheritdoc/>
    public async ValueTask<WStatRecoveryRecord?> GetAsync(
        ResourceOperationId operationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operationId);
        cancellationToken.ThrowIfCancellationRequested();
        WStatRecoveryRecordModel? result = await Grain(operationId.SessionId)
            .GetAsync(operationId.Sequence)
            .WaitAsync(cancellationToken);
        return result?.ToDomain();
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<WStatRecoveryRecord>> GetPendingAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        cancellationToken.ThrowIfCancellationRequested();
        WStatRecoveryRecordModel[] result = await Grain(sessionId)
            .GetPendingAsync()
            .WaitAsync(cancellationToken);
        return result.Select(record => record.ToDomain()).ToArray();
    }

    /// <inheritdoc/>
    public async ValueTask CommitAsync(
        ResourceOperationId operationId,
        string fingerprint,
        uint result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operationId);
        cancellationToken.ThrowIfCancellationRequested();
        await Grain(operationId.SessionId)
            .CommitAsync(operationId.Sequence, fingerprint, result)
            .WaitAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask RejectAsync(
        ResourceOperationId operationId,
        string fingerprint,
        string error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operationId);
        cancellationToken.ThrowIfCancellationRequested();
        await Grain(operationId.SessionId)
            .RejectAsync(operationId.Sequence, fingerprint, error)
            .WaitAsync(cancellationToken);
    }

    private IWStatRecoveryJournalGrain Grain(string sessionId)
        => grainFactory.GetGrain<IWStatRecoveryJournalGrain>(sessionId);
}
