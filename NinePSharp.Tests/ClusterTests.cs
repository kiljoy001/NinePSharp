using NinePSharp.Constants;
using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using Microsoft.Extensions.Logging;
using Moq;
using NinePSharp.Messages;
using NinePSharp.Server.Cluster.Actors;
using NinePSharp.Server.Cluster.Messages;
using NinePSharp.Server.Interfaces;
using Xunit;

namespace NinePSharp.Tests;

public class ClusterTests : TestKit
{
    private readonly Mock<ILogger<BackendRegistryActor>> _loggerMock = new();

    public ClusterTests() : base("akka.actor.provider = cluster")
    {
    }

    [Fact]
    public void Registry_Should_Register_And_Retrieve_Backends()
    {
        var registry = Sys.ActorOf(Props.Create(() => new BackendRegistryActor(_loggerMock.Object)));
        var backendProbe = CreateTestProbe();

        // Register
        registry.Tell(new BackendRegistration("/aws", backendProbe.Ref));

        // Query
        registry.Tell(new GetBackend("/aws"));
        var found = ExpectMsg<BackendFound>();
        Assert.Equal("/aws", found.MountPath);
        Assert.Equal(backendProbe.Ref, found.Actor);

        // Query missing
        registry.Tell(new GetBackend("/missing"));
        var notFound = ExpectMsg<BackendNotFound>();
        Assert.Equal("/missing", notFound.MountPath);
    }

    [Fact]
    public void Supervisor_Should_Spawn_Session()
    {
        var fsMock = new Mock<INinePFileSystem>();

        var supervisor = Sys.ActorOf(Props.Create(() => new BackendSupervisorActor(() => fsMock.Object)));

        supervisor.Tell(new SpawnSession());
        var response = ExpectMsg<SessionSpawned>();
        Assert.NotNull(response.Session);
    }

    [Fact]
    public void SessionActor_Should_Forward_Requests_To_FileSystem()
    {
        var fsMock = new Mock<INinePFileSystem>();
        fsMock.Setup(x => x.WalkAsync(It.IsAny<Twalk>()))
              .ReturnsAsync(new Rwalk(1, Array.Empty<Qid>()));

        var session = Sys.ActorOf(Props.Create(() => new NinePSessionActor(fsMock.Object)));

        // Send DTO
        session.Tell(new TWalkDto { Tag = 1, Fid = 0, NewFid = 1, Wname = Array.Empty<string>() });
        
        var response = ExpectMsg<RWalkDto>();
        Assert.Equal(1, response.Tag);
        
        fsMock.Verify(x => x.WalkAsync(It.IsAny<Twalk>()), Times.Once);
    }
}
