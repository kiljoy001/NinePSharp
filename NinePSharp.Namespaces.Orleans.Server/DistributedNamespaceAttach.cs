using System.Security.Cryptography.X509Certificates;
using NinePSharp.Constants;
using NinePSharp.Messages;

namespace NinePSharp.Namespaces.Orleans.Server;

/// <summary>Resolved namespace and identity for one 9P attach.</summary>
public sealed record DistributedNamespaceAttach(
    string ProcessGroupId,
    long ProcessId,
    string User,
    ResourceHandle Root);

/// <summary>Authenticates and resolves an attach name to an Orleans process namespace.</summary>
public interface IDistributedNamespaceAttachResolver
{
    /// <summary>Resolves a requested attach or throws when access is denied.</summary>
    ValueTask<DistributedNamespaceAttach> ResolveAsync(
        string sessionId,
        Tattach request,
        NinePDialect dialect,
        X509Certificate2? certificate,
        CancellationToken cancellationToken);
}
