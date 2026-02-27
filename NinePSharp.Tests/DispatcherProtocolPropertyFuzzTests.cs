using NinePSharp.Constants;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Protocol;
using NinePSharp.Server;
using NinePSharp.Server.Cluster;
using NinePSharp.Server.Cluster.Actors;
using NinePSharp.Server.Interfaces;
using Xunit;

namespace NinePSharp.Tests;

public class DispatcherProtocolPropertyFuzzTests : IDisposable
{
    private readonly ActorSystem _system = ActorSystem.Create($"DispatcherProtocolPropertyFuzzTests{Guid.NewGuid():N}");

    [Property(MaxTest = 60)]
    public bool Dispatcher_FidBound_Operations_With_UnknownFid_Return_ProtocolError(PositiveInt seed)
    {
        uint fid = (uint)(seed.Get + 1000);
        ushort tagBase = (ushort)((seed.Get % 40000) + 1);

        var dispatcher = BuildDispatcher(Array.Empty<IProtocolBackend>(), new NullClusterManager());
        var messages = BuildUnknownFidMessages(fid, tagBase);

        foreach (var msg in messages)
        {
            var response = dispatcher.DispatchAsync(msg, NinePDialect.NineP2000U).Sync();
            if (response is not Rerror && response is not Rlerror)
            {
                return false;
            }
        }

        return true;
    }

    [Property(MaxTest = 40)]
    public bool Dispatcher_Attach_Matches_Local_Backend_By_MountPath_Or_Name(PositiveInt seed)
    {
        int selector = seed.Get % 3;
        string aname = selector switch
        {
            0 => "/vaultx",
            1 => "vaultx",
            _ => "secretsvc"
        };

        var backend = new Mock<IProtocolBackend>(MockBehavior.Strict);
        backend.SetupGet(b => b.Name).Returns("secretsvc");
        backend.SetupGet(b => b.MountPath).Returns("/vaultx");
        backend.Setup(b => b.GetFileSystem(It.IsAny<SecureString?>(), It.IsAny<X509Certificate2?>()))
            .Returns(new NinePSharp.Server.Backends.MockFileSystem(new NinePSharp.Server.Utils.LuxVaultService()));

        var dispatcher = BuildDispatcher(new[] { backend.Object }, new NullClusterManager());
        var attach = new Tattach(1, 123, NinePConstants.NoFid, "user", aname);

        var response = dispatcher.DispatchAsync(NinePMessage.NewMsgTattach(attach), NinePDialect.NineP2000U).Sync();

        return response is Rattach;
    }

    [Fact]
    public async Task Dispatcher_Empty_AuthFid_Is_Consumed_And_Reused_As_UnknownFid()
    {
        SecureString? capturedCreds = null;

        var backend = new Mock<IProtocolBackend>(MockBehavior.Strict);
        backend.SetupGet(b => b.Name).Returns("mock");
        backend.SetupGet(b => b.MountPath).Returns("/mock");
        backend.Setup(b => b.GetFileSystem(It.IsAny<SecureString?>(), It.IsAny<X509Certificate2?>()))
            .Callback<SecureString?, X509Certificate2?>((c, _) => capturedCreds = c)
            .Returns(new NinePSharp.Server.Backends.MockFileSystem(new NinePSharp.Server.Utils.LuxVaultService()));

        var dispatcher = BuildDispatcher(new[] { backend.Object }, new NullClusterManager());

        uint authFid = 99;
        var authResp = await dispatcher.DispatchAsync(NinePMessage.NewMsgTauth(new Tauth(1, authFid, "u", "/mock")), dialect: NinePDialect.NineP2000U);
        Assert.IsType<Rauth>(authResp);

        var attachResp = await dispatcher.DispatchAsync(
            NinePMessage.NewMsgTattach(new Tattach(2, 100, authFid, "u", "/mock")),
            dialect: NinePDialect.NineP2000U);
        Assert.IsType<Rattach>(attachResp);
        Assert.Null(capturedCreds);

        var writeResp = await dispatcher.DispatchAsync(
            NinePMessage.NewMsgTwrite(new Twrite(3, authFid, 0, Encoding.UTF8.GetBytes("late-secret"))),
            dialect: NinePDialect.NineP2000U);

        var error = Assert.IsType<Rerror>(writeResp);
        Assert.Contains("Unknown FID", error.Ename, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Dispatcher_Remote_Attach_Normalizes_Aname_With_Leading_Slash()
    {
        var sessionActor = _system.ActorOf(Props.Create(() => new SpawnSessionActor()));
        var registryActor = _system.ActorOf(Props.Create(() => new RecordingRegistryActor(sessionActor)));

        var dispatcher = BuildDispatcher(
            Array.Empty<IProtocolBackend>(),
            new StaticClusterManager(_system, registryActor));

        var response = await dispatcher.DispatchAsync(
            NinePMessage.NewMsgTattach(new Tattach(1, 777, NinePConstants.NoFid, "user", "remote")),
            dialect: NinePDialect.NineP2000U);

        Assert.IsType<Rattach>(response);

        var recorded = await registryActor.Ask<string>(new GetLastRequestedPath(), TimeSpan.FromSeconds(2));
        Assert.Equal("/remote", recorded);
    }

    [Fact]
    public async Task Dispatcher_Fuzz_UnknownFid_Sequence_Does_Not_Produce_NonError_Responses()
    {
        var dispatcher = BuildDispatcher(Array.Empty<IProtocolBackend>(), new NullClusterManager());
        var random = new Random(20260226);

        for (int i = 0; i < 240; i++)
        {
            uint fid = (uint)random.Next(500, 25000);
            ushort tag = (ushort)(i + 1);
            var messages = BuildUnknownFidMessages(fid, tag);
            var message = messages[i % messages.Count];

            var response = await dispatcher.DispatchAsync(message, NinePDialect.NineP2000U);
            Assert.True(response is Rerror or Rlerror, $"iteration={i}");
        }
    }

    public void Dispose()
    {
        _system.Terminate().Sync();
        _system.Dispose();
    }

    private static NinePFSDispatcher BuildDispatcher(IEnumerable<IProtocolBackend> backends, IClusterManager clusterManager)
        => new(
            NullLogger<NinePFSDispatcher>.Instance,
            backends,
            clusterManager);

    private static List<NinePMessage> BuildUnknownFidMessages(uint fid, ushort tagBase)
    {
        var stat = new Stat(0, 0, 0, new Qid(QidType.QTFILE, 0, 1), 0644, 0, 0, 0, "x", "u", "g", "m");
        return new List<NinePMessage>
        {
            NinePMessage.NewMsgTwalk(new Twalk((ushort)(tagBase + 0), fid, fid + 1, new[] { "any" })),
            NinePMessage.NewMsgTopen(new Topen((ushort)(tagBase + 1), fid, 0)),
            NinePMessage.NewMsgTread(new Tread((ushort)(tagBase + 2), fid, 0, 64)),
            NinePMessage.NewMsgTwrite(new Twrite((ushort)(tagBase + 3), fid, 0, new byte[] { 1, 2, 3 })),
            NinePMessage.NewMsgTstat(new Tstat((ushort)(tagBase + 4), fid)),
            NinePMessage.NewMsgTcreate(BuildTcreate((ushort)(tagBase + 5), fid, "f", 0644, 0)),
            NinePMessage.NewMsgTwstat(new Twstat((ushort)(tagBase + 6), fid, stat)),
            NinePMessage.NewMsgTremove(new Tremove((ushort)(tagBase + 7), fid))
        };
    }

    private static Tcreate BuildTcreate(ushort tag, uint fid, string name, uint perm, byte mode)
    {
        int nameLen = Encoding.UTF8.GetByteCount(name);
        uint size = (uint)(NinePConstants.HeaderSize + 4 + 2 + nameLen + 4 + 1);
        byte[] data = new byte[size];

        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), size);
        data[4] = (byte)MessageTypes.Tcreate;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(5, 2), tag);

        int offset = NinePConstants.HeaderSize;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), fid);
        offset += 4;

        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset, 2), (ushort)nameLen);
        offset += 2;

        Encoding.UTF8.GetBytes(name).CopyTo(data.AsSpan(offset, nameLen));
        offset += nameLen;

        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), perm);
        offset += 4;

        data[offset] = mode;

        return new Tcreate(data);
    }

    private sealed class NullClusterManager : IClusterManager
    {
        public ActorSystem? System => null;
        public IActorRef? Registry => null;
        public void Start() { }
        public Task StopAsync() => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed class StaticClusterManager : IClusterManager
    {
        public StaticClusterManager(ActorSystem system, IActorRef registry)
        {
            System = system;
            Registry = registry;
        }

        public ActorSystem? System { get; }
        public IActorRef? Registry { get; }
        public void Start() { }
        public Task StopAsync() => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed record GetLastRequestedPath;

    private sealed class RecordingRegistryActor : ReceiveActor
    {
        private readonly IActorRef _backendActor;
        private string _last = string.Empty;

        public RecordingRegistryActor(IActorRef backendActor)
        {
            _backendActor = backendActor;

            Receive<GetBackend>(msg =>
            {
                _last = msg.MountPath;
                Sender.Tell(new BackendFound(msg.MountPath, _backendActor));
            });

            Receive<GetLastRequestedPath>(_ => Sender.Tell(_last));
        }
    }

    private sealed class SpawnSessionActor : ReceiveActor
    {
        public SpawnSessionActor()
        {
            Receive<SpawnSession>(_ => Sender.Tell(new SessionSpawned(Self)));
        }
    }
}
