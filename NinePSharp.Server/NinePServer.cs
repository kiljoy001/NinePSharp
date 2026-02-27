using System;
using System.IO;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Interfaces;
using NinePSharp.Parser;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Cluster;
using NinePSharp.Server.Cluster.Messages;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Akka.Actor;

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
    private readonly INinePFSDispatcher _dispatcher;
    private readonly IClusterManager _clusterManager;
    private readonly IConfiguration _configuration;
    private readonly IEmercoinAuthService _authService;
    private TcpListener? _listener;

    private const uint DefaultMSize = 8192;

    public NinePServer(
        ILogger<NinePServer> logger,
        IOptions<ServerConfig> config,
        IEnumerable<IProtocolBackend> backends,
        INinePFSDispatcher dispatcher,
        IClusterManager clusterManager,
        IConfiguration configuration,
        IEmercoinAuthService authService)
    {
        _logger = logger;
        _config = config.Value;
        _backends = backends;
        _dispatcher = dispatcher;
        _clusterManager = clusterManager;
        _configuration = configuration;
        _authService = authService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("NinePServer.ExecuteAsync started.");
        try 
        {
            // Start Cluster
            _logger.LogInformation("Starting ClusterManager...");
            _clusterManager.Start();
            _logger.LogInformation("ClusterManager started.");

            _logger.LogInformation("Starting backend initialization loop. Total backends: {Count}", _backends.Count());
            foreach (var backend in _backends)
            {
                _logger.LogInformation("Initializing backend: {Name}", backend.Name);
                try
                {
                    var section = _configuration.GetSection($"Server:{backend.Name}");
                    await backend.InitializeAsync(section);
                    _logger.LogInformation("Backend '{BackendName}' initialized at {MountPath}", backend.Name, backend.MountPath);
                    
                    if (_clusterManager.Registry != null)
                    {
                        _logger.LogInformation("Registering backend actor for {Name}...", backend.Name);
                        var backendActor = _clusterManager.System!.ActorOf(Props.Create(() => new Cluster.Actors.BackendSupervisorActor(backend)), $"backend-{backend.Name}");
                        _clusterManager.Registry.Tell(new BackendRegistration(backend.MountPath, backendActor));
                    }
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
                    _ = HandleClientAsync(client, endpoint, stoppingToken);
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

    private class ClientSession
    {
        public string SessionId { get; } = Guid.NewGuid().ToString("N");
        public uint MSize { get; set; } = DefaultMSize;
        public NinePDialect Dialect { get; set; } = NinePDialect.NineP2000;
        public X509Certificate2? ClientCertificate { get; set; }
        public SemaphoreSlim WriteLock { get; } = new(1, 1);
    }

    private async Task HandleClientAsync(TcpClient client, EndpointConfig endpoint, CancellationToken ct)
    {
        var endPoint = client.Client.RemoteEndPoint;
        _logger.LogInformation("Client connected from {EndPoint}", endPoint);
        
        var session = new ClientSession();

        try
        {
            Stream finalStream = client.GetStream();
            if (endpoint.Protocol.Equals("tls", StringComparison.OrdinalIgnoreCase))
            {
                var sslStream = new SslStream(finalStream, false);
                // In a real scenario, you'd load a proper server certificate here.
                // For now, we'll use a placeholder or assume it's handled by the OS/container.
                await sslStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ClientCertificateRequired = true,
                    // RemoteCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true // For testing
                }, ct);
                
                finalStream = sslStream;
                if (sslStream.RemoteCertificate is X509Certificate2 cert)
                {
                    session.ClientCertificate = cert;
                    bool authorized = await _authService.IsCertificateAuthorizedAsync(cert);
                    if (!authorized)
                    {
                        _logger.LogWarning("Client {EndPoint} failed Emercoin authorization.", endPoint);
                        // We could close the connection here, or let the backends decide based on the session state.
                    }
                }
            }

            await using var stream = finalStream;
            var headerBuffer = new byte[NinePConstants.HeaderSize];

            while (!ct.IsCancellationRequested)
            {
                int headerRead = await stream.ReadAtLeastAsync(headerBuffer, headerBuffer.Length, throwOnEndOfStream: false, ct);
                if (headerRead == 0)
                {
                    _logger.LogInformation("Client {EndPoint} disconnected cleanly.", endPoint);
                    break;
                }

                uint size = BinaryPrimitives.ReadUInt32LittleEndian(headerBuffer.AsSpan(0, 4));
                MessageTypes type = (MessageTypes)headerBuffer[4];
                ushort tag = BinaryPrimitives.ReadUInt16LittleEndian(headerBuffer.AsSpan(5, 2));

                if (size < NinePConstants.HeaderSize) 
                {
                    _logger.LogError("Invalid message size {Size} from {EndPoint}", size, endPoint);
                    break;
                }

                uint payloadSize = size - (uint)NinePConstants.HeaderSize;
                byte[] fullMessageBuffer = ArrayPool<byte>.Shared.Rent((int)size);
                headerBuffer.CopyTo(fullMessageBuffer, 0);

                if (payloadSize > 0)
                {
                    int payloadRead = await stream.ReadAtLeastAsync(fullMessageBuffer.AsMemory(NinePConstants.HeaderSize, (int)payloadSize), (int)payloadSize, throwOnEndOfStream: false, ct);
                    if (payloadRead < payloadSize) 
                    {
                        ArrayPool<byte>.Shared.Return(fullMessageBuffer, clearArray: true);
                        break;
                    }
                }

                _logger.LogInformation("Incoming: size={Size}, type={Type}, tag={Tag}", size, type, tag);

                // Fire and forget handling of the message to allow pipelining
                _ = Task.Run(async () => {
                    try {
                        var response = await DispatchMessageAsync(fullMessageBuffer.AsMemory(0, (int)size), type, tag, session);
                        
                        if (response is Rversion rv)
                        {
                            session.MSize = rv.MSize;
                            session.Dialect = Dialect.fromString(rv.Version);
                            _logger.LogInformation("Negotiated MSize: {MSize}, Dialect: {Dialect}, Version: {Version}", session.MSize, session.Dialect, rv.Version);
                        }

                        await SendResponseAsync(stream, response, session.WriteLock);
                    }
                    catch (Exception ex) {
                        _logger.LogError(ex, "Unexpected error during dispatch");
                        // Should probably send Rlerror if .L is negotiated, but standard Rerror for now
                        await SendResponseAsync(stream, new Rerror(tag, ex.Message), session.WriteLock);
                    }
                    finally {
                        ArrayPool<byte>.Shared.Return(fullMessageBuffer, clearArray: true);
                    }
                }, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling client session for {EndPoint}", endPoint);
        }
        finally
        {
            client.Close();
        }
    }

    private async Task<object> DispatchMessageAsync(ReadOnlyMemory<byte> fullMessageBuffer, MessageTypes type, ushort tag, ClientSession session)
    {
        var result = NinePSharp.Parser.NinePParser.parse(session.Dialect, fullMessageBuffer);
        if (result.IsError)
        {
            _logger.LogError("Failed to parse message: {Error}", result.ErrorValue);
            return new Rerror(tag, result.ErrorValue);
        }

        using var _ = NinePFSDispatcherSessionScope.Enter(session.SessionId);
        return await _dispatcher.DispatchAsync(result.ResultValue, session.Dialect, session.ClientCertificate);
    }

    private async Task SendResponseAsync(Stream stream, object response, SemaphoreSlim writeLock)
    {
        if (response is not ISerializable serializable) return;

        byte[] outBuffer = new byte[serializable.Size];
        serializable.WriteTo(outBuffer);

        await writeLock.WaitAsync();
        try {
            await stream.WriteAsync(outBuffer);
        }
        finally {
            writeLock.Release();
        }
    }
}
