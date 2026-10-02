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
