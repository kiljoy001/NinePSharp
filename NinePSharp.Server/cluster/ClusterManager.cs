using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster;
using Akka.Configuration;
using Microsoft.Extensions.Logging;
using NinePSharp.Server.Configuration.Models;

namespace NinePSharp.Server.Cluster;

public class ClusterManager : IClusterManager
{
    private readonly ILogger<ClusterManager> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ServerConfig _config;
    private ActorSystem? _actorSystem;
    public IActorRef? Registry { get; private set; }

    public ClusterManager(ILogger<ClusterManager> logger, ILoggerFactory loggerFactory, ServerConfig config)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _config = config;
    }

    public void Start()
    {
        var akkaConfig = _config.Akka ?? new AkkaConfig();
        
        // Attempt to load from text file if it exists, overriding config.json
        var textConfigPath = Path.Combine(AppContext.BaseDirectory, "cluster.conf");
        if (File.Exists(textConfigPath))
        {
            akkaConfig = ClusterConfigLoader.LoadFromTextFile(textConfigPath, _logger);
        }
        else if (_config.Akka == null)
        {
            _logger.LogInformation("No cluster configuration found (JSON or cluster.conf). Running in standalone mode.");
            return;
        }

        var seeds = string.Join(",", akkaConfig.SeedNodes.Select(s => $"\"{s}\""));
        
        var hocon = $@"
            akka {{
                actor {{
                    provider = cluster
                }}
                remote {{
                    dot-netty.tcp {{
                        hostname = ""{akkaConfig.Hostname}""
                        port = {akkaConfig.Port}
                    }}
                }}
                cluster {{
                    seed-nodes = [{seeds}]
                    roles = [""{akkaConfig.Role}""]
                }}
            }}";

        _logger.LogInformation("Starting Akka System '{SystemName}' on {Host}:{Port}...", akkaConfig.SystemName, akkaConfig.Hostname, akkaConfig.Port);
        
        var config = ConfigurationFactory.ParseString(hocon);
        _actorSystem = ActorSystem.Create(akkaConfig.SystemName, config);
        
        // Create Registry Actor
        Registry = _actorSystem.ActorOf(Props.Create(() => new Actors.BackendRegistryActor(_loggerFactory.CreateLogger<Actors.BackendRegistryActor>())), "registry");

        _logger.LogInformation("Akka System started.");
    }

    public async Task StopAsync()
    {
        if (_actorSystem != null)
        {
            await _actorSystem.Terminate();
            _logger.LogInformation("Akka System terminated.");
        }
    }

    public void Dispose()
    {
        _actorSystem?.Dispose();
    }
    
    public ActorSystem? System => _actorSystem;
}
