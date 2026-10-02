using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using NinePSharp.Client;
using NinePSharp.Constants;
using NinePSharp.Namespaces.Orleans.Server;
using NinePSharp.Namespaces.Orleans.Tests.Support;
using Orleans;
using Orleans.Hosting;
using Orleans.TestingHost;
using Xunit;

namespace NinePSharp.Namespaces.Orleans.Tests;

public sealed class GatewayClusterFixture : IAsyncLifetime
{
    public TestCluster Cluster { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder(2);
        builder.AddSiloBuilderConfigurator<StorageConfigurator>();
        Cluster = builder.Build();
        await Cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        await Cluster.StopAllSilosAsync();
        Cluster.Dispose();
    }

    private sealed class StorageConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder) => siloBuilder.AddMemoryGrainStorageAsDefault();
    }
}
