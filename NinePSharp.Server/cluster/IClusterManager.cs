using Akka.Actor;
using System;
using System.Threading.Tasks;

namespace NinePSharp.Server.Cluster;

public interface IClusterManager : IDisposable
{
    void Start();
    Task StopAsync();
    ActorSystem? System { get; }
    IActorRef? Registry { get; }
}
