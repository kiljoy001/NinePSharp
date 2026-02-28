using System;
using System.Collections.Generic;
using Akka.Actor;

namespace NinePSharp.Server.Cluster.Messages;

public class BackendRegistration
{
    public string MountPath { get; }
    public IActorRef Handler { get; }

    public BackendRegistration(string mountPath, IActorRef handler)
    {
        MountPath = mountPath;
        Handler = handler;
    }
}

public class Remote9PRequest
{
    public long RequestId { get; }
    public object Message { get; } // T-Message

    public Remote9PRequest(long requestId, object message)
    {
        RequestId = requestId;
        Message = message;
    }
}

public class Remote9PResponse
{
    public long RequestId { get; }
    public object Message { get; } // R-Message

    public Remote9PResponse(long requestId, object message)
    {
        RequestId = requestId;
        Message = message;
    }
}
