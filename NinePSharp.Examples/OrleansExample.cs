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

/// <summary>A local, anonymous, read-only example of the embeddable Orleans gateway.</summary>
public static class OrleansExample
{
    public static Task RunAsync(int port)
        => Host.CreateDefaultBuilder()
            .UseOrleans(silo => silo.UseLocalhostClustering().AddMemoryGrainStorageAsDefault())
            .ConfigureServices(services =>
            {
                services.AddNinePOrleans<ExampleAttachResolver>();
                services.AddNinePResource<IExampleResourceGrain>("example");
                services.AddHostedService<ExampleNamespaceBootstrap>();
                services.AddNinePOrleansListener(options => options.Endpoint.Port = port);
            })
            .Build()
            .RunAsync();
}
