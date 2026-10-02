using NinePSharp.Constants;

namespace NinePSharp.Namespaces.Orleans;

/// <summary>Serializable exact-width Plan 9 metadata update.</summary>
[GenerateSerializer]
public sealed record ResourceWStatModel(
    [property: Id(0)] ushort Type,
    [property: Id(1)] uint Device,
    [property: Id(2)] QidType QidType,
    [property: Id(3)] uint QidVersion,
    [property: Id(4)] ulong QidPath,
    [property: Id(5)] uint Mode,
    [property: Id(6)] uint AccessTime,
    [property: Id(7)] uint ModificationTime,
    [property: Id(8)] ulong Length,
    [property: Id(9)] string Name,
    [property: Id(10)] string User,
    [property: Id(11)] string Group,
    [property: Id(12)] string LastModifier,
    [property: Id(13)] uint EncodedLength);
