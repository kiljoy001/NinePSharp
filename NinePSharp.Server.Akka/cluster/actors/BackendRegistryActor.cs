using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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
            RegisterBackend(msg.MountPath, msg.Handler, replicateToPeers: true, preserveLocalIfPresent: false);
        });

        Receive<ReplicatedBackendRegistration>(msg =>
        {
            RegisterBackend(msg.MountPath, msg.Handler, replicateToPeers: false, preserveLocalIfPresent: true);
        });

        Receive<ClusterEvent.MemberUp>(up =>
        {
            _logger.LogInformation("Member is Up: {Member}", up.Member);
            if (up.Member.Address == _cluster.SelfAddress)
            {
                return;
            }

            var remoteRegistry = Context.ActorSelection(new RootActorPath(up.Member.Address) / "user" / "registry");
            foreach (var backend in _backends)
            {
                remoteRegistry.Tell(new ReplicatedBackendRegistration(backend.Key, backend.Value));
            }
        });
        
        Receive<GetBackend>(req =>
        {
            var normalizedPath = NormalizeMountPath(req.MountPath);
            if (_backends.TryGetValue(normalizedPath, out var refActor))
            {
                Sender.Tell(new BackendFound(normalizedPath, refActor));
            }
            else
            {
                Sender.Tell(new BackendNotFound(normalizedPath));
            }
        });

        Receive<GetBackends>(_ =>
        {
            Sender.Tell(new BackendsSnapshot(_backends.Keys.OrderBy(k => k).ToArray()));
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

    private void RegisterBackend(string mountPath, IActorRef handler, bool replicateToPeers, bool preserveLocalIfPresent)
    {
        var normalizedPath = NormalizeMountPath(mountPath);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return;
        }

        if (preserveLocalIfPresent &&
            _backends.TryGetValue(normalizedPath, out var existing) &&
            IsLocalActor(existing))
        {
            return;
        }

        _logger.LogInformation("Registering backend '{MountPath}' from {Sender}", normalizedPath, Sender);
        _backends = _backends.SetItem(normalizedPath, handler);

        if (!replicateToPeers)
        {
            return;
        }

        foreach (var member in _cluster.State.Members.Where(m => m.Status == MemberStatus.Up && m.Address != _cluster.SelfAddress))
        {
            var remoteRegistry = Context.ActorSelection(new RootActorPath(member.Address) / "user" / "registry");
            remoteRegistry.Tell(new ReplicatedBackendRegistration(normalizedPath, handler));
        }
    }

    private bool IsLocalActor(IActorRef actorRef)
    {
        return actorRef.Path.Address == _cluster.SelfAddress || string.IsNullOrEmpty(actorRef.Path.Address.Host);
    }

    private static string NormalizeMountPath(string mountPath)
    {
        if (string.IsNullOrWhiteSpace(mountPath))
        {
            return "/";
        }

        return mountPath.StartsWith("/") ? mountPath : "/" + mountPath;
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

public class GetBackends {}

public class BackendsSnapshot
{
    public IReadOnlyList<string> MountPaths { get; }

    public BackendsSnapshot(IReadOnlyList<string> mountPaths)
    {
        MountPaths = mountPaths;
    }
}

public class ReplicatedBackendRegistration
{
    public string MountPath { get; }
    public IActorRef Handler { get; }

    public ReplicatedBackendRegistration(string mountPath, IActorRef handler)
    {
        MountPath = mountPath;
        Handler = handler;
    }
}
