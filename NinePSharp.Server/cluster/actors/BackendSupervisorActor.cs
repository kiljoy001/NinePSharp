using System;
using Akka.Actor;
using NinePSharp.Server.Cluster.Messages;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Server.Cluster.Actors;

public class BackendSupervisorActor : ReceiveActor
{
    private readonly IProtocolBackend _backend;

    public BackendSupervisorActor(IProtocolBackend backend)
    {
        _backend = backend;

        Receive<SpawnSession>(msg =>
        {
            // 1. Create the FileSystem instance from the backend
            // Note: For now we pass null credentials, or we need to pass them in the message
            // Ideally SpawnSession DTO should contain encrypted credentials
            var fs = _backend.GetFileSystem(null);
            
            // 2. Spawn a NinePSessionActor to manage this FS session
            var sessionActor = Context.ActorOf(Props.Create(() => new NinePSessionActor(fs)));
            
            // 3. Return the ref
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
