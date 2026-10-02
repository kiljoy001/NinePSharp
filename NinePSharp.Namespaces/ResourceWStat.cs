using NinePSharp.Constants;
using NinePSharp.Messages;

namespace NinePSharp.Namespaces;

/// <summary>
/// One decoded Plan 9 wstat request. Width-specific all-ones values and empty
/// strings retain their native unchanged-field meaning.
/// </summary>
public sealed record ResourceWStat(
    ushort Type,
    uint Device,
    Qid Qid,
    uint Mode,
    uint AccessTime,
    uint ModificationTime,
    ulong Length,
    string Name,
    string User,
    string Group,
    string LastModifier,
    uint EncodedLength)
{
    /// <summary>Creates the native nulldir request in which every field is unchanged.</summary>
    public static ResourceWStat Unchanged(uint encodedLength = 49)
        => new(
            ushort.MaxValue,
            uint.MaxValue,
            new Qid((QidType)byte.MaxValue, uint.MaxValue, ulong.MaxValue),
            uint.MaxValue,
            uint.MaxValue,
            uint.MaxValue,
            ulong.MaxValue,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            encodedLength);
}
