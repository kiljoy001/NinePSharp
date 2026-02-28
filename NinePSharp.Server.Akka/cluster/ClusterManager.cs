using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Pattern;
using Akka.Cluster;
using Akka.Configuration;
using Microsoft.Extensions.Logging;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Cluster.Messages;

namespace NinePSharp.Server.Cluster;

public class ClusterManager : IRemoteMountProvider
{
    private readonly ILogger<ClusterManager> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly AkkaConfig? _config;
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);
    private readonly ConcurrentDictionary<string, IActorRef> _backendActors = new(StringComparer.Ordinal);
    private ActorSystem? _actorSystem;
    public IActorRef? Registry { get; private set; }

    public ClusterManager(ILogger<ClusterManager> logger, ILoggerFactory loggerFactory, AkkaConfig? config = null)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _config = config;
    }

    public void Start()
    {
        var akkaConfig = _config ?? new AkkaConfig();
        
        // Attempt to load from text file if it exists, overriding config.json
        var textConfigPath = Path.Combine(AppContext.BaseDirectory, "cluster.conf");
        if (File.Exists(textConfigPath))
        {
            akkaConfig = ClusterConfigLoader.LoadFromTextFile(textConfigPath, _logger);
        }
        else if (_config == null)
        {
            _logger.LogInformation("No cluster configuration found (JSON or cluster.conf). Running in standalone mode.");
            return;
        }

        ApplyInterfaceSelection(akkaConfig);
        var hocon = BuildHocon(akkaConfig);

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

    public Task RegisterMountAsync(string mountPath, Func<INinePFileSystem> createSession)
    {
        ArgumentNullException.ThrowIfNull(createSession);

        if (_actorSystem == null || Registry == null)
        {
            return Task.CompletedTask;
        }

        string normalizedPath = NormalizeMountPath(mountPath);
        string actorName = $"backend-{SanitizeActorName(normalizedPath)}-{Guid.NewGuid():N}";
        var actor = _actorSystem.ActorOf(Props.Create(() => new Actors.BackendSupervisorActor(createSession)), actorName);
        _backendActors[normalizedPath] = actor;
        Registry.Tell(new BackendRegistration(normalizedPath, actor));
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<string>> GetRemoteMountPathsAsync()
    {
        if (Registry == null)
        {
            return Array.Empty<string>();
        }

        try
        {
            var response = await Registry.Ask<object>(new Actors.GetBackends(), _timeout);
            return response is Actors.BackendsSnapshot snapshot
                ? snapshot.MountPaths
                : Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public async Task<INinePFileSystem?> TryCreateRemoteFileSystemAsync(string mountPath)
    {
        if (Registry == null)
        {
            return null;
        }

        string normalizedPath = NormalizeMountPath(mountPath);
        var registryResponse = await Registry.Ask<object>(new Actors.GetBackend(normalizedPath), _timeout);
        if (registryResponse is not Actors.BackendFound found)
        {
            return null;
        }

        var sessionResponse = await found.Actor.Ask<object>(new Actors.SpawnSession(), _timeout);
        if (sessionResponse is not Actors.SessionSpawned spawnedSession)
        {
            return null;
        }

        return new RemoteFileSystem(spawnedSession.Session);
    }

    public static string BuildHocon(AkkaConfig akkaConfig)
    {
        var hostname = Escape(ResolvePublicHostname(akkaConfig));
        var bindHostname = Escape(ResolveBindHostname(akkaConfig));
        var port = ResolvePublicPort(akkaConfig);
        var bindPort = ResolveBindPort(akkaConfig);
        var role = Escape(akkaConfig.Role);
        var seeds = string.Join(",", akkaConfig.SeedNodes.Select(s => $"\"{Escape(s)}\""));

        return $@"
            akka {{
                actor {{
                    provider = cluster
                }}
                remote {{
                    dot-netty.tcp {{
                        hostname = ""{hostname}""
                        port = {port}
                        bind-hostname = ""{bindHostname}""
                        bind-port = {bindPort}
                    }}
                }}
                cluster {{
                    seed-nodes = [{seeds}]
                    roles = [""{role}""]
                }}
            }}";
    }

    public static string? ResolveHostFromInterface(AkkaConfig akkaConfig)
    {
        if (string.IsNullOrWhiteSpace(akkaConfig.InterfaceName))
        {
            return null;
        }

        var iface = NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(nic =>
                nic.Name.Equals(akkaConfig.InterfaceName, StringComparison.OrdinalIgnoreCase) ||
                nic.Id.Equals(akkaConfig.InterfaceName, StringComparison.OrdinalIgnoreCase));
        if (iface == null)
        {
            return null;
        }

        var unicastAddresses = iface.GetIPProperties().UnicastAddresses
            .Select(a => a.Address)
            .Where(addr =>
                !IPAddress.IsLoopback(addr) &&
                (addr.AddressFamily == AddressFamily.InterNetwork || addr.AddressFamily == AddressFamily.InterNetworkV6))
            .Where(addr => !(addr.AddressFamily == AddressFamily.InterNetworkV6 && addr.IsIPv6LinkLocal))
            .ToList();

        if (unicastAddresses.Count == 0)
        {
            return null;
        }

        var preferredFamily = akkaConfig.PreferIPv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
        var selected = unicastAddresses.FirstOrDefault(addr => addr.AddressFamily == preferredFamily)
            ?? unicastAddresses.First();

        return selected.ToString();
    }

    private void ApplyInterfaceSelection(AkkaConfig akkaConfig)
    {
        var resolvedHost = ResolveHostFromInterface(akkaConfig);
        if (string.IsNullOrWhiteSpace(resolvedHost))
        {
            if (!string.IsNullOrWhiteSpace(akkaConfig.InterfaceName))
            {
                _logger.LogWarning("Could not resolve an IP address for interface '{InterfaceName}'. Using configured hostname values.", akkaConfig.InterfaceName);
            }
            return;
        }

        if (string.IsNullOrWhiteSpace(akkaConfig.BindHostname))
        {
            akkaConfig.BindHostname = resolvedHost;
        }

        if (string.IsNullOrWhiteSpace(akkaConfig.PublicHostname))
        {
            akkaConfig.PublicHostname = resolvedHost;
        }

        if (string.IsNullOrWhiteSpace(akkaConfig.Hostname) ||
            string.Equals(akkaConfig.Hostname, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            akkaConfig.Hostname = resolvedHost;
        }

        _logger.LogInformation(
            "Using interface '{InterfaceName}' for cluster transport: bind={BindHost}, public={PublicHost}",
            akkaConfig.InterfaceName,
            ResolveBindHostname(akkaConfig),
            ResolvePublicHostname(akkaConfig));
    }

    private static string ResolvePublicHostname(AkkaConfig akkaConfig)
    {
        return !string.IsNullOrWhiteSpace(akkaConfig.PublicHostname)
            ? akkaConfig.PublicHostname!
            : akkaConfig.Hostname;
    }

    private static string ResolveBindHostname(AkkaConfig akkaConfig)
    {
        return !string.IsNullOrWhiteSpace(akkaConfig.BindHostname)
            ? akkaConfig.BindHostname!
            : akkaConfig.Hostname;
    }

    private static int ResolvePublicPort(AkkaConfig akkaConfig)
    {
        return akkaConfig.PublicPort ?? akkaConfig.Port;
    }

    private static int ResolveBindPort(AkkaConfig akkaConfig)
    {
        return akkaConfig.BindPort ?? akkaConfig.Port;
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string NormalizeMountPath(string mountPath)
    {
        if (string.IsNullOrWhiteSpace(mountPath))
        {
            return "/";
        }

        return mountPath.StartsWith("/", StringComparison.Ordinal) ? mountPath : "/" + mountPath;
    }

    private static string SanitizeActorName(string mountPath)
    {
        return string.Concat(mountPath.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')).Trim('_');
    }
}
