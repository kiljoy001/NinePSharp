using NinePSharp.Constants;

namespace NinePSharp.Namespaces.Orleans;

/// <summary>Serializable provider-owned state associated with an open fid.</summary>
[GenerateSerializer]
public sealed record ResourceOpenHandleModel(
    [property: Id(0)] ResourceHandleModel Resource,
    [property: Id(1)] string HandleId,
    [property: Id(2)] byte Mode,
    [property: Id(3)] uint IoUnit,
    [property: Id(4)] bool IsMountTransport = false);
