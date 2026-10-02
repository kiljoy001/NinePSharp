using NinePSharp.Constants;

namespace NinePSharp.Namespaces.Orleans;

/// <summary>A serializable stable resource identity.</summary>
[GenerateSerializer]
public sealed record ResourceIdentityModel(
    [property: Id(0)] string Provider,
    [property: Id(1)] string Device,
    [property: Id(2)] ulong Path);
