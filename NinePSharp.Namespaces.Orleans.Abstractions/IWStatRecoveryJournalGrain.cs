namespace NinePSharp.Namespaces.Orleans;

/// <summary>Owns the durable wstat recovery journal for one session epoch.</summary>
public interface IWStatRecoveryJournalGrain : IGrainWithStringKey
{
    /// <summary>Durably admits a request or returns its matching existing entry.</summary>
    Task<WStatRecoveryRecordModel> BeginAsync(WStatRecoveryRequestModel request);

    /// <summary>Returns one admitted operation, or null when it does not exist.</summary>
    Task<WStatRecoveryRecordModel?> GetAsync(ulong sequence);

    /// <summary>Returns unresolved entries in operation order.</summary>
    Task<WStatRecoveryRecordModel[]> GetPendingAsync();

    /// <summary>Records a provider result for the matching admitted request.</summary>
    Task CommitAsync(ulong sequence, string fingerprint, uint result);

    /// <summary>Records a definite provider rejection for the matching admitted request.</summary>
    Task RejectAsync(ulong sequence, string fingerprint, string error);
}
