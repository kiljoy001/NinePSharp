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

public sealed class GatewayWireTests(GatewayClusterFixture fixture) : IClassFixture<GatewayClusterFixture>
{
    [Fact]
    public async Task ClientCanReadWriteCreateRemoveAndKeepOpenFidAcrossSiloMigration()
    {
        await using var gateway = await StartGatewayAsync();
        using var client = new NinePClient("127.0.0.1", gateway.Listener.LocalEndpoint.Port);
        Assert.Equal("9P2000", (await client.VersionAsync(1024, "9P2000")).Version);
        await client.AttachAsync(1, NinePConstants.NoFid, "user", "/");
        Assert.Single((await client.WalkAsync(1, 2, new[] { "job" })).Wqid);
        await client.OpenAsync(2, NinePConstants.ORDWR);
        byte[] payload = Encoding.UTF8.GetBytes("over TCP, through Orleans");
        Assert.Equal((uint)payload.Length, (await client.WriteAsync(2, 0, payload)).Count);
        Assert.Equal(payload, (await client.ReadAsync(2, 0, 1024)).Data.ToArray());
        Assert.Equal("job", (await client.StatAsync(2)).Stat.Name);

        var control = fixture.Cluster.GrainFactory.GetGrain<ITestMountableResourceGrain>(gateway.Device);
        TestResourceDiagnostics before = await control.GetDiagnosticsAsync();
        var target = fixture.Cluster.GetActiveSilos().Single(silo =>
            !before.RuntimeIdentity.Contains(silo.SiloAddress.Endpoint.ToString(), StringComparison.Ordinal));
        await fixture.Cluster.MigrateAsync(fixture.Cluster.GrainFactory.GetGrain<IMountableResourceGrain>(gateway.Device), target.SiloAddress)
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True((await control.GetDiagnosticsAsync()).Activations > before.Activations);
        Assert.Equal(payload, (await client.ReadAsync(2, 0, 1024)).Data.ToArray());

        await client.WalkAsync(1, 3, Array.Empty<string>());
        await client.CreateAsync(3, "new", NinePConstants.Mode0644, NinePConstants.ORDWR);
        await client.WriteAsync(3, 0, payload);
        Assert.Equal(payload, (await client.ReadAsync(3, 0, 1024)).Data.ToArray());
        await client.RemoveAsync(3);
        await Assert.ThrowsAsync<NinePException>(() => client.StatAsync(3));
        await Assert.ThrowsAsync<NinePException>(() => client.WalkAsync(1, 4, new[] { "new" }));
        await client.ClunkAsync(2);
        Assert.DoesNotContain("new", (await control.GetDiagnosticsAsync()).Children);
        await client.ClunkAsync(1);
    }

    [Fact]
    public async Task SessionsIsolateFidsAndDisconnectClunksProviderHandles()
    {
        await using var gateway = await StartGatewayAsync();
        using var first = new NinePClient("127.0.0.1", gateway.Listener.LocalEndpoint.Port);
        using var second = new NinePClient("127.0.0.1", gateway.Listener.LocalEndpoint.Port);
        await first.VersionAsync(512, "9P2000");
        await second.VersionAsync(512, "9P2000");
        await first.AttachAsync(1, NinePConstants.NoFid, "user", "/");
        await second.AttachAsync(1, NinePConstants.NoFid, "user", "/");
        await first.WalkAsync(1, 2, new[] { "job" });
        await first.OpenAsync(2, NinePConstants.ORDWR);
        await Assert.ThrowsAsync<NinePException>(() => second.ReadAsync(2, 0, 1));
        first.Dispose();
        var control = fixture.Cluster.GrainFactory.GetGrain<ITestMountableResourceGrain>(gateway.Device);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while ((await control.GetDiagnosticsAsync().WaitAsync(timeout.Token)).Clunks == 0)
        {
            await Task.Delay(10, timeout.Token);
        }

        Assert.Equal("/", (await second.StatAsync(1)).Stat.Name);
        await second.VersionAsync(512, "9P2000");
        await Assert.ThrowsAsync<NinePException>(() => second.StatAsync(1));
        await second.AttachAsync(1, NinePConstants.NoFid, "user", "/");
    }

    private async Task<RunningGateway> StartGatewayAsync()
    {
        string device = Guid.NewGuid().ToString("N");
        string group = Guid.NewGuid().ToString("N");
        await fixture.Cluster.GrainFactory.GetGrain<IVProcessGroupGrain>(group).InitializeEmptyAsync();
        var services = new ServiceCollection();
        services.AddSingleton(fixture.Cluster.GrainFactory);
        services.AddSingleton<IDistributedNamespaceAttachResolver>(new TestAttachResolver
        {
            Group = group,
            Root = new ResourceHandle(new ResourceIdentity("bdd-resource", device, 1), QidType.QTDIR),
        });
        services.AddNinePOrleans<TestAttachResolver>();
        services.AddNinePResource<IMountableResourceGrain>("bdd-resource");
        services.AddNinePOrleansListener(options => options.Endpoint.Port = 0);
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        var listener = provider.GetRequiredService<NinePOrleansListener>();
        await listener.StartAsync(CancellationToken.None);
        return new RunningGateway(provider, listener, device);
    }

    private sealed record RunningGateway(ServiceProvider Services, NinePOrleansListener Listener, string Device) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Listener.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
            await Services.DisposeAsync();
        }
    }
}
