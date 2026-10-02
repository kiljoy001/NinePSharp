using System.Security.Cryptography.X509Certificates;
using Moq;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Namespaces.Orleans.Server;
using NinePSharp.Parser;
using Orleans;
using Xunit;

namespace NinePSharp.Namespaces.Orleans.Tests.Support;

public sealed class TestAttachResolver : IDistributedNamespaceAttachResolver
{
    public string Group { get; set; } = "group";

    public ResourceHandle Root { get; set; } = GatewayTestContext.Root;

    public bool Deny { get; set; }

    public long ProcessId { get; set; } = 1;

    public string User { get; set; } = "user";

    public IResourceOperations? Resources { get; set; }

    public ValueTask<DistributedNamespaceAttach> ResolveAsync(
        string sessionId, Tattach request, NinePDialect dialect, X509Certificate2? certificate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Deny)
        {
            throw new UnauthorizedAccessException("attach denied");
        }

        return ValueTask.FromResult(new DistributedNamespaceAttach(Group, ProcessId, User, Root) { Resources = Resources });
    }
}
