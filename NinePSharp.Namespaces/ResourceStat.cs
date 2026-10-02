using NinePSharp.Constants;
using NinePSharp.Messages;

namespace NinePSharp.Namespaces;

/// <summary>Provider-neutral metadata needed to produce a 9P stat response.</summary>
public sealed record ResourceStat(
    ResourceHandle Resource,
    string Name,
    uint Mode,
    uint AccessTime,
    uint ModificationTime,
    ulong Length,
    string User,
    string Group,
    string LastModifier);
