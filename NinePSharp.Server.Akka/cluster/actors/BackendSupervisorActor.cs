using System;
using Akka.Actor;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Server.Cluster.Actors;

public class BackendSupervisorActor : ReceiveActor
{
    private readonly Func<IBackendRuntime> _createRuntime;

    public BackendSupervisorActor(Func<IBackendRuntime> createRuntime)
    {
        _createRuntime = createRuntime;

        Receive<SpawnSession>(msg =>
        {
            var runtime = _createRuntime();
            
            // Temporary bridge to old session actor which still expects INinePFileSystem.
            // Ideally we'd have a PathBasedSessionActor.
            var sessionActor = Context.ActorOf(Props.Create(() => new NinePSessionActor(new Utils.RuntimeFileSystemAdapter(runtime))));
            
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
