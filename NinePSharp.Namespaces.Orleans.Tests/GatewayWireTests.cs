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
    public async Task DurableWstatJournalEnforcesSessionIdentityAndTerminalTransitions()
    {
        string session = $"journal-{Guid.NewGuid():N}";
        IWStatRecoveryJournalGrain journal = fixture.Cluster.GrainFactory
            .GetGrain<IWStatRecoveryJournalGrain>(session);
        var resource = new ResourceHandle(new ResourceIdentity("bdd-resource", "journal-device", 2), QidType.QTFILE);
        WStatRecoveryRequest Request(ulong sequence, byte value = 1, string? epoch = null)
            => WStatRecoveryRequest.ForResource(
                resource,
                new[] { value },
                new ResourceOperationContext(new ResourceOperationId(epoch ?? session, sequence), 1, "glenda"));

        WStatRecoveryRequest first = Request(1);
        Assert.Equal(WStatRecoveryStateModel.Pending, (await journal.BeginAsync(first.ToModel())).State);
        Assert.Equal(first.Fingerprint, (await journal.BeginAsync(first.ToModel())).Request.Fingerprint);
        await Assert.ThrowsAsync<InvalidOperationException>(() => journal.BeginAsync(Request(1, 2).ToModel()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => journal.BeginAsync(Request(2, epoch: "other").ToModel()));
        Assert.Single(await journal.GetPendingAsync());
        Assert.Null(await journal.GetAsync(99));

        await journal.CommitAsync(1, first.Fingerprint, 1);
        await journal.CommitAsync(1, first.Fingerprint, 1);
        WStatRecoveryRecordModel committed = Assert.IsType<WStatRecoveryRecordModel>(await journal.GetAsync(1));
        Assert.Equal(WStatRecoveryStateModel.Committed, committed.State);
        Assert.Equal(1U, committed.Result);
        Assert.Null(committed.Error);
        Assert.Empty(await journal.GetPendingAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => journal.CommitAsync(1, first.Fingerprint, 2));
        await Assert.ThrowsAsync<InvalidOperationException>(() => journal.RejectAsync(1, first.Fingerprint, "denied"));

        WStatRecoveryRequest second = Request(2);
        await journal.BeginAsync(second.ToModel());
        await journal.RejectAsync(2, second.Fingerprint, "denied");
        await journal.RejectAsync(2, second.Fingerprint, "denied");
        WStatRecoveryRecordModel rejected = Assert.IsType<WStatRecoveryRecordModel>(await journal.GetAsync(2));
        Assert.Equal(WStatRecoveryStateModel.Rejected, rejected.State);
        Assert.Null(rejected.Result);
        Assert.Equal("denied", rejected.Error);
        await Assert.ThrowsAsync<InvalidOperationException>(() => journal.RejectAsync(2, second.Fingerprint, "changed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => journal.CommitAsync(2, second.Fingerprint, 1));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => journal.CommitAsync(99, "missing", 1));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => journal.RejectAsync(99, "missing", "denied"));
        await Assert.ThrowsAsync<ArgumentException>(() => journal.RejectAsync(99, "missing", " "));
    }

    [Fact]
    public async Task NativeWstatIsAtomicAcrossRealGrainTransportAndRetainedHandles()
    {
        string device = Guid.NewGuid().ToString("N");
        var root = new ResourceHandle(new ResourceIdentity("bdd-resource", device, 1), QidType.QTDIR);
        var resources = new OrleansResourceOperations(new RegisteredMountableResourceResolver(
            fixture.Cluster.GrainFactory, new[] { ResourceProviderRegistration.For<IWStatResourceGrain>("bdd-resource") }));
        var table = new VProcessTable();
        var process = table.CreateInitial(NamespaceChannel.Restore(new[] { new ChannelFrame("/", root) }));
        long sequence = 0;
        ResourceOperationContext Context() => new(new(device, (ulong)Interlocked.Increment(ref sequence)), process.Id, "scott");
        var stat = new FileStatOperations(resources, new[] { new DirectoryDeviceBinding(7, 42, "bdd-resource", device) });
        var calls = new Plan9FileSyscalls(process,
            new LocalNamespaceDataPlane(process.ProcessGroup.MountTable, resources), Context, fileStats: stat);
        try
        {
            int fd = await calls.OpenAsync("/job", new(NinePConstants.ORDWR));
            await calls.WriteAsync(fd, new byte[20]);
            byte[] combined = FileStatOperations.EncodeUpdate(ResourceWStat.Unchanged() with
            {
                Name = "final",
                Mode = NinePConstants.Mode0600,
                Length = 3,
            });
            Assert.Equal((uint)combined.Length, await calls.FWStatAsync(fd, combined));
            Assert.Null(await resources.WalkAsync(root, "job", default));
            ResourceHandle renamed = (await resources.WalkAsync(root, "final", default))!;
            ResourceStat changed = await resources.StatAsync(renamed, default);
            Assert.Equal(NinePConstants.Mode0600, changed.Mode);
            Assert.Equal(3UL, changed.Length);
            ReadOnlyMemory<byte> retained = await calls.FStatAsync(fd, 4096);
            Assert.Equal("job", FileStatName(retained));

            await calls.CreateAsync("/taken", new(NinePConstants.Mode0644, NinePConstants.ORDWR));
            byte[] rejected = FileStatOperations.EncodeUpdate(ResourceWStat.Unchanged() with
            {
                Name = "taken",
                Mode = NinePConstants.Mode0644,
            });
            await Assert.ThrowsAsync<ResourceWStatRejectedException>(() => calls.WStatAsync("/final", rejected).AsTask());
            changed = await resources.StatAsync(renamed, default);
            Assert.Equal(NinePConstants.Mode0600, changed.Mode);
            Assert.Equal(renamed, await resources.WalkAsync(root, "final", default));
        }
        finally { await table.TerminateAsync(process.Id); }
    }

    [Fact]
    public async Task NativeFileStatUsesRetainedGrainHandlesAcrossUnmountAndReportsCurrentLength()
    {
        string device = Guid.NewGuid().ToString("N");
        var root = new ResourceHandle(new ResourceIdentity("bdd-resource", device, 1), QidType.QTDIR);
        var resources = new OrleansResourceOperations(new RegisteredMountableResourceResolver(
            fixture.Cluster.GrainFactory, new[] { ResourceProviderRegistration.For<IOpenStatResourceGrain>("bdd-resource") }));
        var table = new VProcessTable();
        var process = table.CreateInitial(NamespaceChannel.Restore(new[] { new ChannelFrame("/", root) }));
        long sequence = 0;
        ResourceOperationContext Context() => new(new(device, (ulong)Interlocked.Increment(ref sequence)), process.Id, "scott");
        var stat = new FileStatOperations(resources, new[] { new DirectoryDeviceBinding(7, 42, "bdd-resource", device) });
        var calls = new Plan9FileSyscalls(process, new LocalNamespaceDataPlane(process.ProcessGroup.MountTable, resources), Context, fileStats: stat);
        try
        {
            int fd = await calls.OpenAsync("/job", new(2));
            await calls.PWriteAsync(fd, 0, new byte[] { 1, 2, 3 });
            var first = await calls.FStatAsync(fd, 2);
            Assert.Equal(2, first.Length);
            uint size = (uint)System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(first.Span) + 2;
            var bytes = await calls.FStatAsync(fd, size);
            int position = 0;
            var decoded = new NinePSharp.Messages.Stat(bytes.Span, ref position);
            Assert.Equal("job", decoded.Name);
            Assert.Equal(3UL, decoded.Length);
            Assert.Equal(0, await calls.SeekAsync(fd, 0, Plan9SeekWhence.Current));
            Assert.Equal(bytes.ToArray(), (await calls.StatAsync("/job", size)).ToArray());

            var job = (await resources.WalkAsync(root, "job", default))!;
            var replacement = (await resources.CreateAndOpenAsync(root, "replacement", 0x180, 2, Context(), default));
            process.ProcessGroup.MountTable.Mount(NamespaceChannel.Restore(new[] { new ChannelFrame("/", replacement.Resource) }), job);
            Assert.Equal(0UL, System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian((await calls.StatAsync("/job", 4096)).Span[33..]));
            Assert.Equal(3UL, System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian((await calls.FStatAsync(fd, 4096)).Span[33..]));
            process.ProcessGroup.MountTable.Unmount(job);
            await resources.ClunkAsync(replacement, Context(), default);
            // This provider invalidates removed resources. Fstat must propagate that policy.
            await resources.RemoveAsync(job, null, Context(), default);
            Assert.Equal("file does not exist", (await Assert.ThrowsAsync<InvalidOperationException>(() => calls.FStatAsync(fd, 4096).AsTask())).Message);
            Assert.Single(process.Descriptors.Snapshot());
        }
        finally { await table.TerminateAsync(process.Id); }
    }

    [Fact]
    public async Task NativeDirectoryStreamUsesRealGrainReadsAndMapsDefiniteRejection()
    {
        string device = Guid.NewGuid().ToString("N");
        var root = new ResourceHandle(new ResourceIdentity("bdd-resource", device, 1), QidType.QTDIR);
        var resources = new OrleansResourceOperations(new RegisteredMountableResourceResolver(
            fixture.Cluster.GrainFactory, new[] { ResourceProviderRegistration.For<IMountableResourceGrain>("bdd-resource") }));
        var table = new VProcessTable();
        var process = table.CreateInitial(NamespaceChannel.Restore(new[] { new ChannelFrame("/", root) }));
        long sequence = 0;
        var calls = new Plan9FileSyscalls(process, new LocalNamespaceDataPlane(process.ProcessGroup.MountTable, resources),
            () => new(new ResourceOperationId(device, (ulong)Interlocked.Increment(ref sequence)), process.Id, "scott"),
            DirectoryReadMode.ProviderStream,
            new DirectoryStatOperations(resources, new[] { new DirectoryDeviceBinding(7, 0, "bdd-resource", device) }));
        try
        {
            int fd = await calls.OpenAsync("/", new(0));
            await Assert.ThrowsAsync<ResourceDirectoryRejectedException>(() => calls.ReadAsync(fd, 1).AsTask());
            var bytes = await calls.ReadAsync(fd, 4096);
            int position = 0;
            Assert.Equal("job", new NinePSharp.Messages.Stat(bytes.Span, ref position).Name);
            Assert.Equal(bytes.Length, position);
            Assert.Empty((await calls.ReadAsync(fd, 4096)).ToArray());
            await calls.CreateAsync("/new", new(0x180, 2));
            bytes = await calls.ReadAsync(fd, 4096);
            position = 0;
            Assert.Equal("new", new NinePSharp.Messages.Stat(bytes.Span, ref position).Name);
            await calls.SeekAsync(fd, 0, Plan9SeekWhence.Set);
            bytes = await calls.ReadAsync(fd, 4096);
            position = 0;
            Assert.Equal("job", new NinePSharp.Messages.Stat(bytes.Span, ref position).Name);
            Assert.Equal("new", new NinePSharp.Messages.Stat(bytes.Span, ref position).Name);
            Assert.Equal(bytes.Length, position);
        }
        finally { await table.TerminateAsync(process.Id); }
    }

    [Fact]
    public async Task DirectorySyscallCursorReadsAndRewindsAnOrleansResourceListing()
    {
        string device = Guid.NewGuid().ToString("N");
        var root = new ResourceHandle(new ResourceIdentity("bdd-resource", device, 1), QidType.QTDIR);
        var resources = new OrleansResourceOperations(new RegisteredMountableResourceResolver(
            fixture.Cluster.GrainFactory, new[] { ResourceProviderRegistration.For<IMountableResourceGrain>("bdd-resource") }));
        var table = new VProcessTable();
        var process = table.CreateInitial(NamespaceChannel.Restore(new[] { new ChannelFrame("/", root) }));
        long sequence = 0;
        var calls = new Plan9FileSyscalls(process, new LocalNamespaceDataPlane(process.ProcessGroup.MountTable, resources),
            () => new(new ResourceOperationId(device, (ulong)Interlocked.Increment(ref sequence)), process.Id, "scott"));
        try
        {
            int directory = await calls.OpenAsync("/", new(0));
            ReadOnlyMemory<byte> bytes = await calls.ReadAsync(directory, 4096);
            int position = 0;
            Assert.Equal("job", new NinePSharp.Messages.Stat(bytes.Span, ref position).Name);
            Assert.Equal(bytes.Length, position);
            await calls.CreateAsync("/new", new(0x180, 2));
            Assert.Empty((await calls.ReadAsync(directory, 4096)).ToArray());
            await calls.SeekAsync(directory, 0, Plan9SeekWhence.Set);
            bytes = await calls.ReadAsync(directory, 4096);
            position = 0;
            Assert.Equal("job", new NinePSharp.Messages.Stat(bytes.Span, ref position).Name);
            Assert.Equal("new", new NinePSharp.Messages.Stat(bytes.Span, ref position).Name);
            Assert.Equal(bytes.Length, position);
        }
        finally { await table.TerminateAsync(process.Id); }
    }

    [Fact]
    public async Task NativeCreateAndDefiniteCollisionWorkAcrossRealResourceGrainTransport()
    {
        string device = Guid.NewGuid().ToString("N");
        var root = new ResourceHandle(new ResourceIdentity("bdd-resource", device, 1), QidType.QTDIR);
        var resources = new OrleansResourceOperations(new RegisteredMountableResourceResolver(
            fixture.Cluster.GrainFactory, new[] { ResourceProviderRegistration.For<IMountableResourceGrain>("bdd-resource") }));
        var table = new VProcessTable();
        var process = table.CreateInitial(NamespaceChannel.Restore(new[] { new ChannelFrame("/", root) }));
        long sequence = 0;
        ResourceOperationContext Context() => new(new ResourceOperationId(device, (ulong)Interlocked.Increment(ref sequence)), process.Id, "scott");
        var calls = new Plan9FileSyscalls(process, new LocalNamespaceDataPlane(process.ProcessGroup.MountTable, resources), Context);
        try
        {
            int original = await calls.CreateAsync("/new", new(NinePConstants.Mode0600, NinePConstants.ORDWR | NinePConstants.OEXCL));
            await calls.WriteAsync(original, "keep"u8.ToArray());
            // Bypass the preliminary syscall walk to exercise the actual serialized rejection.
            var error = await Assert.ThrowsAsync<ResourceCreateRejectedException>(() => resources.CreateAndOpenAsync(
                root, "new", 0, 2, Context(), default).AsTask());
            Assert.Equal("file already exists", error.Message);
            Assert.Equal("keep"u8.ToArray(), (await calls.PReadAsync(original, 0, 10)).ToArray());
            int replacement = await calls.CreateAsync("/new", new(0, NinePConstants.ORDWR));
            Assert.Empty((await calls.ReadAsync(replacement, 10)).ToArray());
            Assert.Equal(process.Descriptors.Snapshot()[original].Handle.Resource.Identity,
                process.Descriptors.Snapshot()[replacement].Handle.Resource.Identity);
        }
        finally { await table.TerminateAsync(process.Id); }
        var diagnostics = await fixture.Cluster.GrainFactory.GetGrain<ITestMountableResourceGrain>(device).GetDiagnosticsAsync();
        Assert.Equal(2, diagnostics.Clunks);
    }

    [Fact]
    public async Task ClientCanReadWriteCreateRemoveAndKeepOpenFidAcrossSiloMigration()
    {
        await using var gateway = await StartGatewayAsync();
        using var client = new NinePClient("127.0.0.1", gateway.Listener.LocalEndpoint.Port);
        Assert.Equal("9P2000", (await Bounded(client.VersionAsync(1024, "9P2000"))).Version);
        await Bounded(client.AttachAsync(1, NinePConstants.NoFid, "user", "/"));
        Assert.Single((await Bounded(client.WalkAsync(1, 2, new[] { "job" }))).Wqid);
        await Bounded(client.OpenAsync(2, NinePConstants.ORDWR));
        byte[] payload = Encoding.UTF8.GetBytes("over TCP, through Orleans");
        Assert.Equal((uint)payload.Length, (await Bounded(client.WriteAsync(2, 0, payload))).Count);
        Assert.Equal(payload, (await Bounded(client.ReadAsync(2, 0, 1024))).Data.ToArray());
        Assert.Equal("job", (await Bounded(client.StatAsync(2))).Stat.Name);

        var control = fixture.Cluster.GrainFactory.GetGrain<ITestMountableResourceGrain>(gateway.Device);
        TestResourceDiagnostics before = await control.GetDiagnosticsAsync();
        var target = fixture.Cluster.GetActiveSilos().Single(silo =>
            !before.RuntimeIdentity.Contains(silo.SiloAddress.Endpoint.ToString(), StringComparison.Ordinal));
        await fixture.Cluster.MigrateAsync(fixture.Cluster.GrainFactory.GetGrain<IMountableResourceGrain>(gateway.Device), target.SiloAddress)
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True((await control.GetDiagnosticsAsync()).Activations > before.Activations);
        Assert.Equal(payload, (await Bounded(client.ReadAsync(2, 0, 1024))).Data.ToArray());

        await Bounded(client.WalkAsync(1, 3, Array.Empty<string>()));
        await Bounded(client.CreateAsync(3, "new", NinePConstants.Mode0644, NinePConstants.ORDWR));
        await Bounded(client.WriteAsync(3, 0, payload));
        Assert.Equal(payload, (await Bounded(client.ReadAsync(3, 0, 1024))).Data.ToArray());
        await Bounded(client.RemoveAsync(3));
        await Assert.ThrowsAsync<NinePException>(() => Bounded(client.StatAsync(3)));
        await Assert.ThrowsAsync<NinePException>(() => Bounded(client.WalkAsync(1, 4, new[] { "new" })));
        await Bounded(client.ClunkAsync(2));
        Assert.DoesNotContain("new", (await control.GetDiagnosticsAsync()).Children);
        await Bounded(client.ClunkAsync(1));
    }

    [Fact]
    public async Task SessionsIsolateFidsAndDisconnectClunksProviderHandles()
    {
        await using var gateway = await StartGatewayAsync();
        using var first = new NinePClient("127.0.0.1", gateway.Listener.LocalEndpoint.Port);
        using var second = new NinePClient("127.0.0.1", gateway.Listener.LocalEndpoint.Port);
        await Bounded(first.VersionAsync(512, "9P2000"));
        await Bounded(second.VersionAsync(512, "9P2000"));
        await Bounded(first.AttachAsync(1, NinePConstants.NoFid, "user", "/"));
        await Bounded(second.AttachAsync(1, NinePConstants.NoFid, "user", "/"));
        await Bounded(first.WalkAsync(1, 2, new[] { "job" }));
        await Bounded(first.OpenAsync(2, NinePConstants.ORDWR));
        await Assert.ThrowsAsync<NinePException>(() => Bounded(second.ReadAsync(2, 0, 1)));
        first.Dispose();
        var control = fixture.Cluster.GrainFactory.GetGrain<ITestMountableResourceGrain>(gateway.Device);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while ((await control.GetDiagnosticsAsync().WaitAsync(timeout.Token)).Clunks == 0)
        {
            await Task.Delay(10, timeout.Token);
        }

        Assert.Equal("/", (await Bounded(second.StatAsync(1))).Stat.Name);
        await Bounded(second.VersionAsync(512, "9P2000"));
        await Assert.ThrowsAsync<NinePException>(() => Bounded(second.StatAsync(1)));
        await Bounded(second.AttachAsync(1, NinePConstants.NoFid, "user", "/"));
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

    private static Task<T> Bounded<T>(Task<T> task)
        => task.WaitAsync(TimeSpan.FromSeconds(5));

    private static Task Bounded(Task task)
        => task.WaitAsync(TimeSpan.FromSeconds(5));

    private static string FileStatName(ReadOnlyMemory<byte> record)
    {
        int length = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(record.Span[41..]);
        return Encoding.UTF8.GetString(record.Span.Slice(43, length));
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
