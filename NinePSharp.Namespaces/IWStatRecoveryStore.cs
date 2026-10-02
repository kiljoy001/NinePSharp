using System.Security.Cryptography;
using System.Text;

namespace NinePSharp.Namespaces;

/// <summary>Persists metadata mutation intents and terminal outcomes across host failure.</summary>
public interface IWStatRecoveryStore
{
    /// <summary>Creates an intent or returns the matching existing operation.</summary>
    ValueTask<WStatRecoveryRecord> BeginAsync(WStatRecoveryRequest request, CancellationToken cancellationToken);

    /// <summary>Returns one operation by its stable identity.</summary>
    ValueTask<WStatRecoveryRecord?> GetAsync(ResourceOperationId operationId, CancellationToken cancellationToken);

    /// <summary>Returns unresolved operations for one session epoch.</summary>
    ValueTask<IReadOnlyList<WStatRecoveryRecord>> GetPendingAsync(string sessionId, CancellationToken cancellationToken);

    /// <summary>Persists the provider result if the fingerprint still identifies the admitted request.</summary>
    ValueTask CommitAsync(
        ResourceOperationId operationId,
        string fingerprint,
        uint result,
        CancellationToken cancellationToken);

    /// <summary>Persists a definite provider rejection without applying or retrying the mutation.</summary>
    ValueTask RejectAsync(
        ResourceOperationId operationId,
        string fingerprint,
        string error,
        CancellationToken cancellationToken);
}
