using NinePSharp.Constants;
using NinePSharp.Messages;

namespace NinePSharp.Namespaces;

/// <summary>
/// A channel target containing stable identity and current Qid metadata.
/// </summary>
/// <param name="Identity">The stable object identity.</param>
/// <param name="Type">The Qid type bits.</param>
/// <param name="Version">The current Qid version.</param>
public sealed record ResourceHandle(ResourceIdentity Identity, QidType Type, uint Version = 0)
{
    /// <summary>Gets a value indicating whether the resource is a directory.</summary>
    public bool IsDirectory => (Type & QidType.QTDIR) != 0;

    /// <summary>Gets the wire-level Qid.</summary>
    public Qid Qid => new(Type, Version, Identity.Path);
}
