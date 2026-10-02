using NinePSharp.Constants;

namespace NinePSharp.Namespaces.Orleans;

/// <summary>Serializable provider-neutral metadata for a 9P stat response.</summary>
[GenerateSerializer]
public sealed record ResourceStatModel(
    [property: Id(0)] ResourceHandleModel Resource,
    [property: Id(1)] string Name,
    [property: Id(2)] uint Mode,
    [property: Id(3)] uint AccessTime,
    [property: Id(4)] uint ModificationTime,
    [property: Id(5)] ulong Length,
    [property: Id(6)] string User,
    [property: Id(7)] string Group,
    [property: Id(8)] string LastModifier);
