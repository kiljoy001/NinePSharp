namespace NinePSharp.Namespaces.Orleans;

/// <summary>Persistent recovery records owned by one namespace session epoch.</summary>
[GenerateSerializer]
public sealed class WStatRecoveryPersistentState
{
    /// <summary>Gets or sets entries keyed by their operation sequence.</summary>
    [Id(0)]
    public Dictionary<ulong, WStatRecoveryRecordModel> Records { get; set; } = new();
}
