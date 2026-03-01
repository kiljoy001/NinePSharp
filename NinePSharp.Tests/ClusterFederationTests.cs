using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Server;
using NinePSharp.Server.Cluster;
using NinePSharp.Server.Cluster.Actors;
using NinePSharp.Server.Cluster.Messages;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Tests;

public class ClusterFederationTests : TestKit
{
    private readonly Mock<ILoggerFactory> _loggerFactoryMock = new();

    public ClusterFederationTests() : base("akka.actor.provider = cluster")
    {
        _loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(NullLogger.Instance);
    }

    [Fact]
    public async Task Federation_Should_Resolve_Remote_Mounts()
    {
        var systemName = "FederationTest" + Guid.NewGuid().ToString("N")[..8];
        var config = new AkkaConfig
        {
            SystemName = systemName,
            Hostname = "127.0.0.1",
            Port = 0,
            Role = "backend"
        };

        using var cluster = new ClusterManager(NullLogger<ClusterManager>.Instance, _loggerFactoryMock.Object, config);
        cluster.Start();

        var fsMock = new Mock<INinePFileSystem>();
        fsMock.SetupProperty(f => f.Dialect);
        fsMock.Setup(x => x.WalkAsync(It.IsAny<Twalk>())).ReturnsAsync(new Rwalk(1, new[] { new Qid(QidType.QTDIR, 0, 1) }));
        fsMock.Setup(x => x.Clone()).Returns(fsMock.Object);

        await cluster.RegisterMountAsync("/remote", () => RuntimeFileSystemAdapter.ToRuntime(fsMock.Object));

        var dispatcher = new NinePFSDispatcher(NullLogger<NinePFSDispatcher>.Instance, Array.Empty<IProtocolBackend>(), cluster);

        // Attach
        var attach = new Tattach(1, 1, uint.MaxValue, "user", "/");
        await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTattach(attach), NinePDialect.NineP2000);

        // Walk to remote
        var walk = new Twalk(2, 1, 2, new[] { "remote" });
        var response = await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTwalk(walk), NinePDialect.NineP2000);

        response.Should().BeOfType<Rwalk>();
        var rwalk = (Rwalk)response;
        rwalk.Wqid.Should().HaveCount(1);
    }
}
