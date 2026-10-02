using NinePSharp.Constants;

namespace NinePSharp.Namespaces.Orleans;

/// <summary>Serializable authenticated process context for a resource operation.</summary>
[GenerateSerializer]
public sealed record ResourceOperationContextModel(
    [property: Id(0)] ResourceOperationIdModel OperationId,
    [property: Id(1)] long ProcessId,
    [property: Id(2)] string User);
