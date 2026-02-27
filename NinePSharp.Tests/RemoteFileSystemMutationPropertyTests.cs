using NinePSharp.Constants;
using System;
using System.Linq;
using Akka.Actor;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using NinePSharp.Messages;
using NinePSharp.Server.Cluster;
using NinePSharp.Server.Cluster.Messages;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Tests;

public class RemoteFileSystemMutationPropertyTests : IDisposable
{
    private readonly ActorSystem _system = ActorSystem.Create($"RemoteFileSystemMutationPropertyTests{Guid.NewGuid():N}");

    public void Dispose()
    {
        _system.Terminate().Sync();
        _system.Dispose();
    }

    [Fact]
    public void RemoteFileSystem_Stat_Rejects_Null_Stat_Payload()
    {
        var session = _system.ActorOf(Props.Create(() => new NullStatPayloadSessionActor()));
        var sut = new RemoteFileSystem(session);

        Action act = () => sut.StatAsync(new Tstat(1, 42)).Sync();

        act.Should()
            .Throw<NinePProtocolException>()
            .Which.ErrorMessage.Should()
            .Contain("empty");
    }

    [Property(MaxTest = 25)]
    public void RemoteFileSystem_Stat_Parses_DotU_Extensions(NonEmptyString rawName, ushort tag, uint fid)
    {
        var safeName = new string(rawName.Get
            .Where(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-')
            .Take(20)
            .ToArray());
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "dotu";
        }

        var session = _system.ActorOf(Props.Create(() => new DotUStatPayloadSessionActor(safeName)));
        var sut = new RemoteFileSystem(session);

        var stat = sut.StatAsync(new Tstat(tag, fid)).Sync();

        stat.Tag.Should().Be(tag);
        stat.Stat.Dialect.Should().Be(NinePDialect.NineP2000U);
        stat.Stat.Name.Should().Be(safeName);
        stat.Stat.Extension.Should().Be("ext");
        stat.Stat.NUid.Should().Be(1000u);
        stat.Stat.NGid.Should().Be(1001u);
        stat.Stat.NMuid.Should().Be(1002u);
    }

    private sealed class NullStatPayloadSessionActor : ReceiveActor
    {
        public NullStatPayloadSessionActor()
        {
            Receive<TStatDto>(msg =>
            {
                Sender.Tell(new RStatDto
                {
                    Tag = msg.Tag,
                    Dialect = NinePDialect.NineP2000,
                    StatBytes = null!
                });
            });
        }
    }

    private sealed class DotUStatPayloadSessionActor : ReceiveActor
    {
        private readonly string _name;

        public DotUStatPayloadSessionActor(string name)
        {
            _name = name;
            Receive<TStatDto>(msg =>
            {
                var stat = new Stat(
                    size: 0,
                    type: 0,
                    dev: 0,
                    qid: new Qid(QidType.QTFILE, 1, 123),
                    mode: 0644,
                    atime: 0,
                    mtime: 0,
                    length: 7,
                    name: _name,
                    uid: "u",
                    gid: "g",
                    muid: "m",
                    dialect: NinePDialect.NineP2000U,
                    extension: "ext",
                    nUid: 1000,
                    nGid: 1001,
                    nMuid: 1002);

                Sender.Tell(new RStatDto(new Rstat(msg.Tag, stat)));
            });
        }
    }
}
