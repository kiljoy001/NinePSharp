using System.Security.Cryptography;
using System.Text;

namespace NinePSharp.Namespaces;

/// <summary>An in-process journal useful for local hosts and deterministic tests.</summary>
public sealed class MemoryWStatRecoveryStore : IWStatRecoveryStore
{
    private readonly object gate = new();
    private readonly Dictionary<ResourceOperationId, WStatRecoveryRecord> records = new();

    /// <inheritdoc/>
    public ValueTask<WStatRecoveryRecord> BeginAsync(WStatRecoveryRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            ResourceOperationId id = request.Context.OperationId;
            if (records.TryGetValue(id, out WStatRecoveryRecord? existing))
            {
                EnsureFingerprint(existing, request.Fingerprint);
                return ValueTask.FromResult(existing);
            }

            var created = new WStatRecoveryRecord(request, WStatRecoveryState.Pending);
            records.Add(id, created);
            return ValueTask.FromResult(created);
        }
    }

    /// <inheritdoc/>
    public ValueTask<WStatRecoveryRecord?> GetAsync(ResourceOperationId operationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            records.TryGetValue(operationId, out WStatRecoveryRecord? record);
            return ValueTask.FromResult(record);
        }
    }

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<WStatRecoveryRecord>> GetPendingAsync(string sessionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            IReadOnlyList<WStatRecoveryRecord> pending = records.Values
                .Where(record => record.State == WStatRecoveryState.Pending
                    && record.Request.Context.OperationId.SessionId == sessionId)
                .OrderBy(record => record.Request.Context.OperationId.Sequence)
                .ToArray();
            return ValueTask.FromResult(pending);
        }
    }

    /// <inheritdoc/>
    public ValueTask CommitAsync(
        ResourceOperationId operationId,
        string fingerprint,
        uint result,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            WStatRecoveryRecord record = Require(operationId, fingerprint);
            if (record.State == WStatRecoveryState.Rejected)
            {
                throw new InvalidOperationException("A rejected wstat operation cannot be committed.");
            }

            if (record.State == WStatRecoveryState.Committed)
            {
                if (record.Result != result)
                {
                    throw new InvalidOperationException("A committed wstat operation cannot change its result.");
                }

                return ValueTask.CompletedTask;
            }

            records[operationId] = record with { State = WStatRecoveryState.Committed, Result = result, Error = null };
            return ValueTask.CompletedTask;
        }
    }

    /// <inheritdoc/>
    public ValueTask RejectAsync(
        ResourceOperationId operationId,
        string fingerprint,
        string error,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            WStatRecoveryRecord record = Require(operationId, fingerprint);
            if (record.State == WStatRecoveryState.Committed)
            {
                throw new InvalidOperationException("A committed wstat operation cannot be rejected.");
            }

            if (record.State == WStatRecoveryState.Rejected)
            {
                if (!string.Equals(record.Error, error, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("A rejected wstat operation cannot change its error.");
                }

                return ValueTask.CompletedTask;
            }

            records[operationId] = record with { State = WStatRecoveryState.Rejected, Result = null, Error = error };
            return ValueTask.CompletedTask;
        }
    }

    private static void EnsureFingerprint(WStatRecoveryRecord record, string fingerprint)
    {
        if (!string.Equals(record.Request.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("An operation identity was reused for a different wstat request.");
        }
    }

    private WStatRecoveryRecord Require(ResourceOperationId operationId, string fingerprint)
    {
        if (!records.TryGetValue(operationId, out WStatRecoveryRecord? record))
        {
            throw new KeyNotFoundException("The wstat operation was not durably admitted.");
        }

        EnsureFingerprint(record, fingerprint);
        return record;
    }
}
