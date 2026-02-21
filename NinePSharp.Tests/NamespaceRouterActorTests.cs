using System;
using Akka.Actor;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using Xunit;
using NinePSharp.Messages;
using NinePSharp.Constants;

namespace NinePSharp.Tests;

// --- TDD Envelopes & Mocks (Assumed existing in referenced assemblies) ---

public class ClusterRequest
{
    public string Path { get; }
    public object Payload { get; }

    public ClusterRequest(string path, object payload)
    {
        Path = path;
        Payload = payload;
    }
}

public class ClusterResponse
{
    public object Payload { get; }

    public ClusterResponse(object payload)
    {
        Payload = payload;
    }
}

public class MountRequest
{
    public string MountPath { get; }
    public IActorRef ProviderConfig { get; }

    public MountRequest(string mountPath, IActorRef providerConfig)
    {
        MountPath = mountPath;
        ProviderConfig = providerConfig;
    }
}

// No longer needed

// --- Actor Implementations ---

public class NamespaceRouterActor : ReceiveActor
{
    private readonly Dictionary<string, IActorRef> _mounts = new Dictionary<string, IActorRef>();

    public NamespaceRouterActor()
    {
        Receive<MountRequest>(mount =>
        {
            _mounts[mount.MountPath] = mount.ProviderConfig;
            Sender.Tell(new ClusterResponse(true));
        });

        Receive<ClusterRequest>(req =>
        {
            IActorRef targetProvider = null;
            string matchedPath = "";

            // Find longest matching mount path
            foreach (var mount in _mounts.Keys)
            {
                if (req.Path.StartsWith(mount) && mount.Length > matchedPath.Length)
                {
                    matchedPath = mount;
                    targetProvider = _mounts[mount];
                }
            }

            if (targetProvider != null)
            {
                targetProvider.Forward(req);
            }
            else
            {
                // Reply with error if no mount found
                Sender.Tell(new ClusterResponse(new Rerror(NinePConstants.NoTag, "file not found")));
            }
        });
    }
}

// --- xUnit Specifications ---

public class NamespaceRouterActorTests : TestKit
{
    private readonly IActorRef _router;
    private readonly TestProbe _emercoinProvider;

    public NamespaceRouterActorTests()
    {
        _router = Sys.ActorOf(Props.Create(() => new NamespaceRouterActor()), "namespace-router");
        _emercoinProvider = CreateTestProbe("emercoin-provider");

        _router.Tell(new MountRequest("/n/emercoin", _emercoinProvider.Ref), TestActor);
    }

    [Fact]
    public void Router_Should_Forward_Base_9P2000_Messages_To_Mounted_Provider()
    {
        // Arrange
        var twalkPayload = new Twalk();
        var request = new ClusterRequest("/n/emercoin/dns", twalkPayload);

        // Act
        ExpectMsg<ClusterResponse>(); // Drain mount ACK
        _router.Tell(request, TestActor);

        // Assert
        var forwardedMsg = _emercoinProvider.ExpectMsg<ClusterRequest>();
        forwardedMsg.Path.Should().Be("/n/emercoin/dns");
        forwardedMsg.Payload.Should().BeOfType<Twalk>();
        
        // The router must preserve the original sender for the backend to reply directly
        _emercoinProvider.Sender.Should().Be(TestActor);
    }

    [Fact]
    public void Router_Should_Forward_Linux_9P2000L_Messages_To_Mounted_Provider()
    {
        // Arrange
        var tgetattrPayload = new NinePSharp.Messages.Tgetattr();
        var request = new ClusterRequest("/n/emercoin/dns", tgetattrPayload);

        // Act
        ExpectMsg<ClusterResponse>(); // Drain mount ACK
        _router.Tell(request, TestActor);

        // Assert
        var forwardedMsg = _emercoinProvider.ExpectMsg<ClusterRequest>();
        forwardedMsg.Path.Should().Be("/n/emercoin/dns");
        forwardedMsg.Payload.Should().BeOfType<NinePSharp.Messages.Tgetattr>();
        
        _emercoinProvider.Sender.Should().Be(TestActor);
    }

    [Fact]
    public void Router_Should_Reply_With_Rerror_For_Unmounted_Paths()
    {
        // Arrange
        var twalkPayload = new Twalk();
        var request = new ClusterRequest("/n/invalid", twalkPayload);

        // Act
        ExpectMsg<ClusterResponse>(); // Drain mount ACK
        _router.Tell(request, TestActor);

        // Assert
        var errorResponse = ExpectMsg<ClusterResponse>();
        errorResponse.Payload.Should().BeOfType<Rerror>();
        
        var rerror = (Rerror)errorResponse.Payload;
        rerror.Ename.Should().ContainEquivalentOf("not found");
        
        // Ensure the mock provider was successfully isolated and received no bleed traffic
        _emercoinProvider.ExpectNoMsg(TimeSpan.FromMilliseconds(50));
    }
}
