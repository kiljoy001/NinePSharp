using NinePSharp.Constants;

namespace NinePSharp.Namespaces.Orleans;

/// <summary>A serializable identity for one replayable resource operation.</summary>
[GenerateSerializer]
public sealed record ResourceOperationIdModel(
    [property: Id(0)] string SessionId,
    [property: Id(1)] ulong Sequence);
