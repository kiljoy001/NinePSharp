using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster;
using Microsoft.Extensions.Logging;
using NinePSharp.Server.Cluster.Messages;

namespace NinePSharp.Server.Cluster.Actors;

public class BackendRegistryActor : ReceiveActor
{
    private readonly Akka.Cluster.Cluster _cluster = Akka.Cluster.Cluster.Get(Context.System);
    private readonly ILogger<BackendRegistryActor> _logger;
    
    // Map: MountPath -> ActorRef (Remote Proxy or Direct Ref)
    private ImmutableDictionary<string, IActorRef> _backends = ImmutableDictionary<string, IActorRef>.Empty;

    public BackendRegistryActor(ILogger<BackendRegistryActor> logger)
    {
        _logger = logger;

        Receive<BackendRegistration>(msg =>
        {
            _logger.LogInformation("Registering backend '{MountPath}' from {Sender}", msg.MountPath, Sender);
            _backends = _backends.SetItem(msg.MountPath, msg.Handler);
        });

        Receive<ClusterEvent.MemberUp>(up =>
        {
            _logger.LogInformation("Member is Up: {Member}", up.Member);
            // In a full impl, we'd sync registry state here
        });
        
        Receive<GetBackend>(req =>
        {
            if (_backends.TryGetValue(req.MountPath, out var refActor))
            {
                Sender.Tell(new BackendFound(req.MountPath, refActor));
            }
            else
            {
                Sender.Tell(new BackendNotFound(req.MountPath));
            }
        });
    }

    protected override void PreStart()
    {
        _cluster.Subscribe(Self, ClusterEvent.InitialStateAsEvents, typeof(ClusterEvent.MemberUp), typeof(ClusterEvent.MemberRemoved));
    }

    protected override void PostStop()
    {
        _cluster.Unsubscribe(Self);
    }
}

public class GetBackend
{
    public string MountPath { get; }
    public GetBackend(string path) => MountPath = path;
}

public class BackendFound
{
    public string MountPath { get; }
    public IActorRef Actor { get; }
    public BackendFound(string path, IActorRef actor)
    {
        MountPath = path;
        Actor = actor;
    }
}

public class BackendNotFound
{
    public string MountPath { get; }
    public BackendNotFound(string path) => MountPath = path;
}
