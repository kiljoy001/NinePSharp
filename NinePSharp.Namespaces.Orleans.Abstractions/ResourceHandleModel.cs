using NinePSharp.Constants;

namespace NinePSharp.Namespaces.Orleans;

/// <summary>A serializable resource handle.</summary>
[GenerateSerializer]
public sealed record ResourceHandleModel(
    [property: Id(0)] ResourceIdentityModel Identity,
    [property: Id(1)] QidType Type,
    [property: Id(2)] uint Version);
