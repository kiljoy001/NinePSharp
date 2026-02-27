using NinePSharp.Constants;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Protocol;
using NinePSharp.Server;
using NinePSharp.Server.Cluster;
using NinePSharp.Server.Cluster.Actors;
using NinePSharp.Server.Cluster.Messages;
using NinePSharp.Server.Interfaces;
using Xunit;

namespace NinePSharp.Tests;

public class ClusterFederationTests : IDisposable
{
    private readonly ActorSystem _system;

    public ClusterFederationTests()
    {
        _system = ActorSystem.Create("ClusterFederationTests");
    }

    public void Dispose()
    {
        _system.Terminate().Sync();
    }

    [Fact]
    public async Task RootListing_Includes_ClusterRegisteredMounts()
    {
        var sessionActor = _system.ActorOf(Props.Create(() => new FakeSessionActor()));
        var backendActor = _system.ActorOf(Props.Create(() => new FakeBackendSupervisorActor(sessionActor)));
        var registryActor = _system.ActorOf(Props.Create(() => new FakeRegistryActor(backendActor)));

        var dispatcher = CreateDispatcher(registryActor);

        var attach = await dispatcher.DispatchAsync(
            NinePMessage.NewMsgTattach(new Tattach(1, 100, NinePConstants.NoFid, "scott", "/")),
            dialect: NinePDialect.NineP2000);
        Assert.IsType<Rattach>(attach);

        var read = await dispatcher.DispatchAsync(
            NinePMessage.NewMsgTread(new Tread(2, 100, 0, 8192)),
            dialect: NinePDialect.NineP2000);
        var rread = Assert.IsType<Rread>(read);

        var names = ParseDirectory(rread.Data.ToArray()).Select(s => s.Name).ToArray();
        Assert.Contains("jsonrpc", names);
    }

    [Fact]
    public async Task RootWalk_RemoteMount_DelegatesToRemoteSession()
    {
        var sessionActor = _system.ActorOf(Props.Create(() => new FakeSessionActor()));
        var backendActor = _system.ActorOf(Props.Create(() => new FakeBackendSupervisorActor(sessionActor)));
        var registryActor = _system.ActorOf(Props.Create(() => new FakeRegistryActor(backendActor)));

        var dispatcher = CreateDispatcher(registryActor);

        var attach = await dispatcher.DispatchAsync(
            NinePMessage.NewMsgTattach(new Tattach(1, 100, NinePConstants.NoFid, "scott", "/")),
            dialect: NinePDialect.NineP2000);
        Assert.IsType<Rattach>(attach);

        var walk = await dispatcher.DispatchAsync(
            NinePMessage.NewMsgTwalk(new Twalk(2, 100, 101, new[] { "jsonrpc", "chaininfo" })),
            dialect: NinePDialect.NineP2000);
        var rwalk = Assert.IsType<Rwalk>(walk);
        Assert.Equal(2, rwalk.Wqid.Length);

        var open = await dispatcher.DispatchAsync(
            NinePMessage.NewMsgTopen(new Topen(3, 101, NinePConstants.OREAD)),
            dialect: NinePDialect.NineP2000);
        Assert.IsType<Ropen>(open);

        var read = await dispatcher.DispatchAsync(
            NinePMessage.NewMsgTread(new Tread(4, 101, 0, 4096)),
            dialect: NinePDialect.NineP2000);
        var rread = Assert.IsType<Rread>(read);
        Assert.Equal("remote-ok", Encoding.UTF8.GetString(rread.Data.ToArray()));
    }

    [Fact]
    public async Task RootWalk_RemoteMount_StatRoundTrips()
    {
        var sessionActor = _system.ActorOf(Props.Create(() => new FakeSessionActor()));
        var backendActor = _system.ActorOf(Props.Create(() => new FakeBackendSupervisorActor(sessionActor)));
        var registryActor = _system.ActorOf(Props.Create(() => new FakeRegistryActor(backendActor)));

        var dispatcher = CreateDispatcher(registryActor);

        var attach = await dispatcher.DispatchAsync(
            NinePMessage.NewMsgTattach(new Tattach(1, 100, NinePConstants.NoFid, "scott", "/")),
            dialect: NinePDialect.NineP2000);
        Assert.IsType<Rattach>(attach);

        var walk = await dispatcher.DispatchAsync(
            NinePMessage.NewMsgTwalk(new Twalk(2, 100, 101, new[] { "jsonrpc", "chaininfo" })),
            dialect: NinePDialect.NineP2000);
        Assert.IsType<Rwalk>(walk);

        var stat = await dispatcher.DispatchAsync(
            NinePMessage.NewMsgTstat(new Tstat(3, 101)),
            dialect: NinePDialect.NineP2000);
        var rstat = Assert.IsType<Rstat>(stat);
        Assert.Equal("chaininfo", rstat.Stat.Name);
    }

    private NinePFSDispatcher CreateDispatcher(IActorRef registryActor)
    {
        return new NinePFSDispatcher(
            new NullLogger(),
            Array.Empty<IProtocolBackend>(),
            new FakeClusterManager(_system, registryActor));
    }

    private static Stat[] ParseDirectory(byte[] data)
    {
        var result = new System.Collections.Generic.List<Stat>();
        int offset = 0;
        while (offset < data.Length)
        {
            result.Add(new Stat(data, ref offset));
        }
        return result.ToArray();
    }

    private sealed class FakeClusterManager : IClusterManager
    {
        public ActorSystem? System { get; }
        public IActorRef? Registry { get; }

        public FakeClusterManager(ActorSystem system, IActorRef registry)
        {
            System = system;
            Registry = registry;
        }

        public void Start() { }
        public Task StopAsync() => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed class FakeRegistryActor : ReceiveActor
    {
        private readonly IActorRef _backendActor;

        public FakeRegistryActor(IActorRef backendActor)
        {
            _backendActor = backendActor;

            Receive<GetBackends>(_ => Sender.Tell(new BackendsSnapshot(new[] { "/jsonrpc" })));
            Receive<GetBackend>(msg =>
            {
                if (msg.MountPath == "/jsonrpc")
                {
                    Sender.Tell(new BackendFound("/jsonrpc", _backendActor));
                }
                else
                {
                    Sender.Tell(new BackendNotFound(msg.MountPath));
                }
            });
        }
    }

    private sealed class FakeBackendSupervisorActor : ReceiveActor
    {
        private readonly IActorRef _sessionActor;

        public FakeBackendSupervisorActor(IActorRef sessionActor)
        {
            _sessionActor = sessionActor;
            Receive<SpawnSession>(_ => Sender.Tell(new SessionSpawned(_sessionActor)));
        }
    }

    private sealed class FakeSessionActor : ReceiveActor
    {
        public FakeSessionActor()
        {
            Receive<TWalkDto>(msg =>
            {
                var qids = msg.Wname.Select((name, index) =>
                    new Qid(index == msg.Wname.Length - 1 ? QidType.QTFILE : QidType.QTDIR, 0, (ulong)name.GetHashCode()))
                    .ToArray();

                Sender.Tell(new RWalkDto
                {
                    Tag = msg.Tag,
                    Wqid = qids
                });
            });

            Receive<TOpenDto>(msg =>
            {
                Sender.Tell(new ROpenDto
                {
                    Tag = msg.Tag,
                    Qid = new Qid(QidType.QTFILE, 0, 1),
                    Iounit = 0
                });
            });

            Receive<TReadDto>(msg =>
            {
                Sender.Tell(new RReadDto
                {
                    Tag = msg.Tag,
                    Data = Encoding.UTF8.GetBytes("remote-ok")
                });
            });

            Receive<TStatDto>(msg =>
            {
                var stat = new Stat(0, 0, 0, new Qid(QidType.QTFILE, 0, 1), 0644, 0, 0, 0, "chaininfo", "scott", "scott", "scott");
                var statBytes = new byte[stat.Size];
                int offset = 0;
                stat.WriteTo(statBytes, ref offset);

                Sender.Tell(new RStatDto
                {
                    Tag = msg.Tag,
                    Dialect = NinePDialect.NineP2000,
                    StatBytes = statBytes
                });
            });

            Receive<TClunkDto>(msg => Sender.Tell(new RClunkDto { Tag = msg.Tag }));
        }
    }

    private sealed class NullLogger : Microsoft.Extensions.Logging.ILogger<NinePFSDispatcher>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
