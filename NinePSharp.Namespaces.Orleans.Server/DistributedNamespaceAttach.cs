using System.Security.Cryptography.X509Certificates;
using NinePSharp.Constants;
using NinePSharp.Messages;

namespace NinePSharp.Namespaces.Orleans.Server;

/// <summary>Resolved namespace and identity for one 9P attach.</summary>
public sealed record DistributedNamespaceAttach(
    string ProcessGroupId,
    long ProcessId,
    string User,
    ResourceHandle Root)
{
    /// <summary>
    /// Gets resource operations for this attach's session only, such as an authorization view for its
    /// principal; null uses the dispatcher's shared operations.
    /// </summary>
    public IResourceOperations? Resources { get; init; }
}
