using NinePSharp.Constants;

namespace NinePSharp.Namespaces.Orleans;

/// <summary>A serializable durable metadata-mutation journal entry.</summary>
[GenerateSerializer]
public sealed record WStatRecoveryRecordModel(
    [property: Id(0)] WStatRecoveryRequestModel Request,
    [property: Id(1)] WStatRecoveryStateModel State,
    [property: Id(2)] uint? Result,
    [property: Id(3)] string? Error);
