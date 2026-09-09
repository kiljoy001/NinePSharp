using NinePSharp.Constants;
using NinePSharp.Messages;

namespace NinePSharp.Namespaces;

/// <summary>
/// Identifies an object independently of the textual path used to reach it.
/// </summary>
public sealed record ResourceIdentity
{
    /// <summary>Initializes a new instance of the <see cref="ResourceIdentity"/> class.</summary>
    /// <param name="provider">The resource provider or grain type.</param>
    /// <param name="device">The provider instance or grain key.</param>
    /// <param name="path">The provider-assigned stable Qid path.</param>
    public ResourceIdentity(string provider, string device, ulong path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(device);

        Provider = provider;
        Device = device;
        Path = path;
    }

    /// <summary>Gets the resource provider or grain type.</summary>
    public string Provider { get; }

    /// <summary>Gets the provider instance or grain key.</summary>
    public string Device { get; }

    /// <summary>Gets the provider-assigned stable Qid path.</summary>
    public ulong Path { get; }
}

/// <summary>
/// A channel target containing stable identity and current Qid metadata.
/// </summary>
/// <param name="Identity">The stable object identity.</param>
/// <param name="Type">The Qid type bits.</param>
/// <param name="Version">The current Qid version.</param>
public sealed record ResourceHandle(ResourceIdentity Identity, QidType Type, uint Version = 0)
{
    /// <summary>Gets whether the resource is a directory.</summary>
    public bool IsDirectory => (Type & QidType.QTDIR) != 0;

    /// <summary>Gets the wire-level Qid.</summary>
    public Qid Qid => new(Type, Version, Identity.Path);
}

/// <summary>A named directory entry returned by a resource provider.</summary>
/// <param name="Name">The visible entry name.</param>
/// <param name="Handle">The resource represented by the entry.</param>
public sealed record ResourceDirectoryEntry(string Name, ResourceHandle Handle);
