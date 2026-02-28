using System;
using Akka.Actor;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Server.Cluster.Actors;

public class BackendSupervisorActor : ReceiveActor
{
    private readonly Func<INinePFileSystem> _createSession;

    public BackendSupervisorActor(Func<INinePFileSystem> createSession)
    {
        _createSession = createSession;

        Receive<SpawnSession>(msg =>
        {
            var fs = _createSession();
            
            var sessionActor = Context.ActorOf(Props.Create(() => new NinePSessionActor(fs)));
            
            Sender.Tell(new SessionSpawned(sessionActor));
        });
    }
}

public class SpawnSession {}
public class SessionSpawned
{
    public IActorRef Session { get; }
    public SessionSpawned(IActorRef session) => Session = session;
}
