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

public sealed class ExampleNamespaceBootstrap(IGrainFactory grains) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await grains.GetGrain<IVProcessGroupGrain>("example").InitializeEmptyAsync().WaitAsync(cancellationToken);
        var root = new NamespaceChannelModel(new[]
        {
            new ChannelFrameModel("/", new ResourceHandleModel(new ResourceIdentityModel("example", "hello", 1), QidType.QTDIR, 0), null, null),
        });
        await grains.GetGrain<IVProcessGrain>(1)
            .InitializeAsync(new VProcessStateModel(1, null, "example", root, root)).WaitAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
