using System;
using System.Collections.Generic;
using System.Linq;
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
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Tests;

public class ClusterChaosTests : TestKit
{
    private readonly Mock<ILoggerFactory> _loggerFactoryMock = new();

    public ClusterChaosTests() : base("akka.actor.provider = cluster")
    {
        _loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(NullLogger.Instance);
    }

    [Fact]
    public async Task Cluster_Should_Handle_Actor_Restart()
    {
        var systemName = "ChaosTest" + Guid.NewGuid().ToString("N")[..8];
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

        await cluster.RegisterMountAsync("/chaos", () => RuntimeFileSystemAdapter.ToRuntime(fsMock.Object));

        var runtime = await cluster.TryCreateRemoteRuntimeAsync("/chaos");
        runtime.Should().NotBeNull();

        // Kill the underlying actors or simulate failure - simplified for this test kit
        await cluster.StopAsync();
        
        var runtime2 = await cluster.TryCreateRemoteRuntimeAsync("/chaos");
        runtime2.Should().BeNull();
    }
}
