using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Namespaces;
using NinePSharp.Namespaces.Orleans;
using NinePSharp.Namespaces.Orleans.Server;
using Orleans;
using Orleans.Hosting;

namespace NinePSharp.Examples;

/// <summary>Explicit anonymous policy for the loopback-only example; production hosts supply their own policy.</summary>
public sealed class ExampleAttachResolver(IGrainFactory grains) : IDistributedNamespaceAttachResolver
{
    public async ValueTask<DistributedNamespaceAttach> ResolveAsync(
        string sessionId, Tattach request, NinePDialect dialect, X509Certificate2? certificate, CancellationToken cancellationToken)
    {
        if (request.Afid != NinePConstants.NoFid || request.Uname != "guest" || request.Aname is not ("" or "/"))
        {
            throw new UnauthorizedAccessException("Attach to / as guest for the local example.");
        }

        VProcessStateModel process = await grains.GetGrain<IVProcessGrain>(1).GetStateAsync().WaitAsync(cancellationToken);
        return new DistributedNamespaceAttach(process.ProcessGroupId, process.ProcessId, "guest", process.Root.ToDomain().Current);
    }
}
