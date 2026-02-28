using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NinePSharp.Constants;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Server;

/// <summary>
/// The main NinePSharp server implementation, running as a hosted background service.
/// It handles TCP/TLS connections, protocol negotiation, and dispatches messages to backends.
/// </summary>
public class NinePServer : BackgroundService
{
    private readonly ILogger<NinePServer> _logger;
    private readonly ServerConfig _config;
    private readonly IEnumerable<IProtocolBackend> _backends;
    private readonly IRemoteMountProvider _remoteMountProvider;
    private readonly IConfiguration _configuration;
    private readonly NinePConnectionProcessor _connectionProcessor;
    private TcpListener? _listener;

    public NinePServer(
        ILogger<NinePServer> logger,
        IOptions<ServerConfig> config,
        IEnumerable<IProtocolBackend> backends,
        INinePFSDispatcher dispatcher,
        IRemoteMountProvider remoteMountProvider,
        IConfiguration configuration,
        IEmercoinAuthService authService)
    {
        _logger = logger;
        _config = config.Value;
        _backends = backends;
        _remoteMountProvider = remoteMountProvider;
        _configuration = configuration;
        _connectionProcessor = new NinePConnectionProcessor(logger, dispatcher, authService);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("NinePServer.ExecuteAsync started.");
        try 
        {
            _logger.LogInformation("Starting remote mount provider...");
            _remoteMountProvider.Start();
            _logger.LogInformation("Remote mount provider started.");

            _logger.LogInformation("Starting backend initialization loop. Total backends: {Count}", _backends.Count());
            foreach (var backend in _backends)
            {
                _logger.LogInformation("Initializing backend: {Name}", backend.Name);
                try
                {
                    var section = _configuration.GetSection($"Server:{backend.Name}");
                    await backend.InitializeAsync(section);
                    _logger.LogInformation("Backend '{BackendName}' initialized at {MountPath}", backend.Name, backend.MountPath);

                    await _remoteMountProvider.RegisterMountAsync(backend.MountPath, () => backend.GetFileSystem((System.Security.Cryptography.X509Certificates.X509Certificate2?)null));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize backend '{BackendName}'", backend.Name);
                }
            }
            _logger.LogInformation("Backend initialization loop finished.");

            _logger.LogInformation("Checking configuration for endpoints...");
            if (_config.Endpoints == null)
            {
                _logger.LogCritical("Server:Endpoints section is missing from configuration!");
                return;
            }
            _logger.LogInformation("Endpoints count: {Count}", _config.Endpoints.Count);

            var endpoint = _config.Endpoints.Count > 0 
                ? _config.Endpoints[0] 
                : null;

            if (endpoint == null)
            {
                _logger.LogWarning("No endpoints configured for 9P server.");
                return;
            }

            _logger.LogInformation("Creating TcpListener on {Address}:{Port}", endpoint.Address, endpoint.Port);
            _listener = new TcpListener(IPAddress.Parse(endpoint.Address), endpoint.Port);
            _logger.LogInformation("Starting TcpListener...");
            _listener.Start();
            _logger.LogInformation("9P Server listening on {Address}:{Port} ({Protocol})...", endpoint.Address, endpoint.Port, endpoint.Protocol);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync(stoppingToken);
                    _ = _connectionProcessor.HandleClientAsync(client, endpoint, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error accepting client connection.");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "9P Server crashed during startup or execution loop");
            throw;
        }
        finally
        {
            _listener?.Stop();
        }
    }
}
