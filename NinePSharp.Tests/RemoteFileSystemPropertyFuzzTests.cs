using NinePSharp.Constants;
using System;
using System.Buffers.Binary;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using NinePSharp.Messages;
using NinePSharp.Server.Cluster.Actors;
using NinePSharp.Server.Cluster.Messages;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Tests;

public class RemoteFileSystemPropertyFuzzTests : IDisposable
{
    private readonly ActorSystem _system = ActorSystem.Create($"RemoteFileSystemPropertyFuzzTests{Guid.NewGuid():N}");

    public void Dispose()
    {
        _system.Terminate().Sync();
    }

    [Property(MaxTest = 35)]
    public void RemoteFileSystem_Successfully_Maps_All_Core_Operations(
        ushort tag,
        uint fid,
        uint newFid,
        ulong offset,
        PositiveInt countSeed,
        byte mode,
        ulong getattrMask,
        uint setattrValid)
    {
        uint count = (uint)(countSeed.Get % 64 + 1);
        var names = new[] { "alpha", "beta" };
        var writePayload = Enumerable.Range(0, (int)count).Select(i => (byte)((i * 29 + 11) % 251)).ToArray();

        var session = _system.ActorOf(Props.Create(() => new SuccessSessionActor()));
        var sut = new RemoteFileSystem(session);

        var walk = sut.WalkAsync(new Twalk(tag, fid, newFid, names)).Sync();
        walk.Tag.Should().Be(tag);
        walk.Wqid.Length.Should().Be(names.Length);
        walk.Wqid[0].Type.Should().Be(QidType.QTDIR);
        walk.Wqid[^1].Type.Should().Be(QidType.QTFILE);

        var read = sut.ReadAsync(new Tread(tag, fid, offset, count)).Sync();
        read.Tag.Should().Be(tag);
        read.Data.ToArray().Should().Equal(BuildReadPayload(fid, offset, count));

        var write = sut.WriteAsync(new Twrite(tag, fid, offset, writePayload)).Sync();
        write.Tag.Should().Be(tag);
        write.Count.Should().Be((uint)writePayload.Length);

        var open = sut.OpenAsync(new Topen(tag, fid, mode)).Sync();
        open.Tag.Should().Be(tag);
        open.Qid.Type.Should().Be(QidType.QTFILE);
        open.Iounit.Should().Be((uint)(1024 + mode));

        var clunk = sut.ClunkAsync(new Tclunk(tag, fid)).Sync();
        clunk.Tag.Should().Be(tag);

        var stat = sut.StatAsync(new Tstat(tag, fid)).Sync();
        stat.Tag.Should().Be(tag);
        stat.Stat.Name.Should().Be("remote-stat");
        stat.Stat.Mode.Should().Be(0644u);

        var wstatRequest = new Twstat(tag, fid, CreateStat("client-stat"));
        var wstat = sut.WstatAsync(wstatRequest).Sync();
        wstat.Tag.Should().Be(tag);

        var remove = sut.RemoveAsync(new Tremove(tag, fid)).Sync();
        remove.Tag.Should().Be(tag);

        var getattr = sut.GetAttrAsync(new Tgetattr(tag, fid, getattrMask)).Sync();
        getattr.Tag.Should().Be(tag);
        getattr.Valid.Should().Be(getattrMask);
        getattr.Qid.Path.Should().Be(fid);
        getattr.Mode.Should().Be(0644u);

        var setattrRequest = new Tsetattr(0, tag, fid, setattrValid, 0640, 1000, 1001, 8192, 10, 20, 30, 40);
        var setattr = sut.SetAttrAsync(setattrRequest).Sync();
        setattr.Tag.Should().Be(tag);
    }

    [Property(MaxTest = 20)]
    public void RemoteFileSystem_Maps_RErrorDto_To_NinePProtocolException(NonEmptyString errorName, ushort tag, uint fid)
    {
        string error = errorName.Get;
        var session = _system.ActorOf(Props.Create(() => new AlwaysErrorSessionActor(error)));
        var sut = new RemoteFileSystem(session);

        AssertProtocolError(() => sut.WalkAsync(new Twalk(tag, fid, fid + 1, new[] { "x" })), error);
        AssertProtocolError(() => sut.ReadAsync(new Tread(tag, fid, 0, 1)), error);
        AssertProtocolError(() => sut.WriteAsync(new Twrite(tag, fid, 0, new byte[] { 1 })), error);
        AssertProtocolError(() => sut.OpenAsync(new Topen(tag, fid, NinePConstants.OREAD)), error);
        AssertProtocolError(() => sut.ClunkAsync(new Tclunk(tag, fid)), error);
        AssertProtocolError(() => sut.StatAsync(new Tstat(tag, fid)), error);
        AssertProtocolError(() => sut.WstatAsync(new Twstat(tag, fid, CreateStat("w"))), error);
        AssertProtocolError(() => sut.RemoveAsync(new Tremove(tag, fid)), error);
        AssertProtocolError(() => sut.GetAttrAsync(new Tgetattr(tag, fid, 0xFF)), error);
        AssertProtocolError(() => sut.SetAttrAsync(new Tsetattr(0, tag, fid, 0xF, 0644, 1, 2, 3, 4, 5, 6, 7)), error);
    }

    [Fact]
    public void RemoteFileSystem_Rejects_Unexpected_Response_Types()
    {
        var session = _system.ActorOf(Props.Create(() => new UnexpectedResponseSessionActor()));
        var sut = new RemoteFileSystem(session);

        AssertUnexpected(() => sut.WalkAsync(new Twalk(1, 2, 3, new[] { "x" })));
        AssertUnexpected(() => sut.ReadAsync(new Tread(1, 2, 0, 4)));
        AssertUnexpected(() => sut.WriteAsync(new Twrite(1, 2, 0, new byte[] { 1, 2 })));
        AssertUnexpected(() => sut.OpenAsync(new Topen(1, 2, NinePConstants.OREAD)));
        AssertUnexpected(() => sut.ClunkAsync(new Tclunk(1, 2)));
        AssertUnexpected(() => sut.StatAsync(new Tstat(1, 2)));
        AssertUnexpected(() => sut.WstatAsync(new Twstat(1, 2, CreateStat("w"))));
        AssertUnexpected(() => sut.RemoveAsync(new Tremove(1, 2)));
        AssertUnexpected(() => sut.GetAttrAsync(new Tgetattr(1, 2, 1)));
        AssertUnexpected(() => sut.SetAttrAsync(new Tsetattr(0, 1, 2, 1, 0644, 1, 1, 1, 1, 1, 1, 1)));
    }

    [Fact]
    public void RemoteFileSystem_Stat_Rejects_Empty_Stat_Payload()
    {
        var session = _system.ActorOf(Props.Create(() => new EmptyStatSessionActor()));
        var sut = new RemoteFileSystem(session);

        Action act = () => sut.StatAsync(new Tstat(7, 9)).Sync();
        act.Should()
            .Throw<NinePProtocolException>()
            .Which.ErrorMessage.Should()
            .Contain("empty");
    }

    [Fact]
    public void RemoteFileSystem_Stat_Fuzz_Invalid_Buffers_Never_Returns_Success()
    {
        var random = new Random(1729);

        for (int i = 0; i < 80; i++)
        {
            int len = random.Next(2, 32);
            var payload = new byte[len];
            random.NextBytes(payload);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0, 2), (ushort)(len + 24)); // force impossible declared size

            var actor = _system.ActorOf(Props.Create(() => new InvalidStatPayloadSessionActor(payload)));
            var sut = new RemoteFileSystem(actor);

            Action act = () => sut.StatAsync(new Tstat((ushort)i, 1)).Sync();
            act.Should().Throw<Exception>();
        }
    }

    [Fact]
    public void RemoteFileSystem_Clone_Spawns_Independent_Remote_Session()
    {
        var session = _system.ActorOf(Props.Create(() => new CloneCapableSessionActor()));
        var sut = new RemoteFileSystem(session);

        var originalWalk = sut.WalkAsync(new Twalk(1, 1, 2, new[] { "x" })).Sync();
        originalWalk.Wqid.Should().ContainSingle();
        originalWalk.Wqid[0].Path.Should().Be(111UL);

        var clone = sut.Clone();
        var cloneWalk = clone.WalkAsync(new Twalk(2, 1, 3, new[] { "x" })).Sync();
        cloneWalk.Wqid.Should().ContainSingle();
        cloneWalk.Wqid[0].Path.Should().Be(999UL);
    }

    [Fact]
    public void RemoteFileSystem_Clone_Throws_When_SpawnClone_Response_Is_Invalid()
    {
        var session = _system.ActorOf(Props.Create(() => new CloneFailureSessionActor()));
        var sut = new RemoteFileSystem(session);

        Action act = () => sut.Clone();
        act.Should().Throw<Exception>().WithMessage("*Failed to clone remote filesystem session*");
    }

    private static byte[] BuildReadPayload(uint fid, ulong offset, uint count)
    {
        int len = (int)Math.Min(count, 64);
        var data = new byte[len];
        for (int i = 0; i < len; i++)
        {
            data[i] = (byte)((fid + offset + (ulong)i) % 251);
        }

        return data;
    }

    private static Stat CreateStat(string name)
    {
        return new Stat(
            0,
            0,
            0,
            new Qid(QidType.QTFILE, 0, 42),
            0644,
            1,
            2,
            128,
            name,
            "u",
            "g",
            "m");
    }

    private static void AssertUnexpected<T>(Func<Task<T>> action)
    {
        Action act = () => action().Sync();
        act.Should().Throw<Exception>().WithMessage("*Unexpected response*");
    }

    private static void AssertProtocolError<T>(Func<Task<T>> action, string expected)
    {
        Action act = () => action().Sync();
        act.Should().Throw<NinePProtocolException>()
            .Which.ErrorMessage.Should().Contain(expected);
    }

    private sealed class SuccessSessionActor : ReceiveActor
    {
        public SuccessSessionActor()
        {
            Receive<TWalkDto>(msg =>
            {
                var qids = msg.Wname.Select((_, index) =>
                    new Qid(index == msg.Wname.Length - 1 ? QidType.QTFILE : QidType.QTDIR, 0, (ulong)(index + 1)))
                    .ToArray();
                Sender.Tell(new RWalkDto { Tag = msg.Tag, Wqid = qids });
            });

            Receive<TReadDto>(msg =>
            {
                Sender.Tell(new RReadDto
                {
                    Tag = msg.Tag,
                    Data = BuildReadPayload(msg.Fid, msg.Offset, msg.Count)
                });
            });

            Receive<TWriteDto>(msg => Sender.Tell(new RWriteDto { Tag = msg.Tag, Count = (uint)msg.Data.Length }));
            Receive<TOpenDto>(msg => Sender.Tell(new ROpenDto
            {
                Tag = msg.Tag,
                Qid = new Qid(QidType.QTFILE, 0, msg.Fid),
                Iounit = (uint)(1024 + msg.Mode)
            }));
            Receive<TClunkDto>(msg => Sender.Tell(new RClunkDto { Tag = msg.Tag }));
            Receive<TRemoveDto>(msg => Sender.Tell(new RRemoveDto { Tag = msg.Tag }));
            Receive<TSetAttrDto>(msg => Sender.Tell(new RSetAttrDto { Tag = msg.Tag }));
            Receive<TWstatDto>(msg => Sender.Tell(new RWstatDto { Tag = msg.Tag }));

            Receive<TStatDto>(msg =>
            {
                Sender.Tell(new RStatDto(new Rstat(msg.Tag, CreateStat("remote-stat"))));
            });

            Receive<TGetAttrDto>(msg =>
            {
                Sender.Tell(new RGetAttrDto(new Rgetattr(
                    msg.Tag,
                    msg.RequestMask,
                    new Qid(QidType.QTFILE, 0, msg.Fid),
                    0644,
                    1000,
                    1001,
                    1,
                    0,
                    256,
                    4096,
                    1,
                    10,
                    20,
                    30,
                    40,
                    50,
                    60,
                    70,
                    80,
                    90,
                    100)));
            });
        }
    }

    private sealed class AlwaysErrorSessionActor : ReceiveActor
    {
        private readonly string _error;

        public AlwaysErrorSessionActor(string error)
        {
            _error = error;
            ReceiveAny(msg =>
            {
                ushort tag = msg switch
                {
                    TWalkDto m => m.Tag,
                    TReadDto m => m.Tag,
                    TWriteDto m => m.Tag,
                    TOpenDto m => m.Tag,
                    TClunkDto m => m.Tag,
                    TStatDto m => m.Tag,
                    TWstatDto m => m.Tag,
                    TRemoveDto m => m.Tag,
                    TGetAttrDto m => m.Tag,
                    TSetAttrDto m => m.Tag,
                    _ => 0
                };

                Sender.Tell(new RErrorDto { Tag = tag, Ename = _error });
            });
        }
    }

    private sealed class UnexpectedResponseSessionActor : ReceiveActor
    {
        public UnexpectedResponseSessionActor()
        {
            ReceiveAny(_ => Sender.Tell("not-a-dto"));
        }
    }

    private sealed class EmptyStatSessionActor : ReceiveActor
    {
        public EmptyStatSessionActor()
        {
            Receive<TStatDto>(msg => Sender.Tell(new RStatDto { Tag = msg.Tag, Dialect = NinePDialect.NineP2000, StatBytes = Array.Empty<byte>() }));
        }
    }

    private sealed class InvalidStatPayloadSessionActor : ReceiveActor
    {
        private readonly byte[] _payload;

        public InvalidStatPayloadSessionActor(byte[] payload)
        {
            _payload = payload;
            Receive<TStatDto>(msg => Sender.Tell(new RStatDto { Tag = msg.Tag, Dialect = NinePDialect.NineP2000, StatBytes = _payload }));
        }
    }

    private sealed class CloneCapableSessionActor : ReceiveActor
    {
        public CloneCapableSessionActor()
        {
            Receive<TWalkDto>(msg =>
            {
                Sender.Tell(new RWalkDto
                {
                    Tag = msg.Tag,
                    Wqid = new[] { new Qid(QidType.QTFILE, 0, 111UL) }
                });
            });

            Receive<SpawnClone>(_ =>
            {
                var cloneActor = Context.ActorOf(Props.Create(() => new ClonedSessionActor()));
                Sender.Tell(new CloneSpawned(cloneActor));
            });
        }
    }

    private sealed class ClonedSessionActor : ReceiveActor
    {
        public ClonedSessionActor()
        {
            Receive<TWalkDto>(msg =>
            {
                Sender.Tell(new RWalkDto
                {
                    Tag = msg.Tag,
                    Wqid = new[] { new Qid(QidType.QTFILE, 0, 999UL) }
                });
            });
        }
    }

    private sealed class CloneFailureSessionActor : ReceiveActor
    {
        public CloneFailureSessionActor()
        {
            Receive<SpawnClone>(_ => Sender.Tell("clone-not-spawned"));
        }
    }
}
