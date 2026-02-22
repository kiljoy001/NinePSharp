using System;
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
using Akka.Actor;

namespace NinePSharp.Server;

public class NinePServer : BackgroundService
{
    private readonly ILogger<NinePServer> _logger;
    private readonly ServerConfig _config;
    private readonly IEnumerable<IProtocolBackend> _backends;
    private readonly INinePFSDispatcher _dispatcher;
    private readonly IClusterManager _clusterManager;
    private readonly IConfiguration _configuration;
    private TcpListener? _listener;

    private const uint DefaultMSize = 8192;

    public NinePServer(
        ILogger<NinePServer> logger,
        IOptions<ServerConfig> config,
        IEnumerable<IProtocolBackend> backends,
        INinePFSDispatcher dispatcher,
        IClusterManager clusterManager,
        IConfiguration configuration)
    {
        _logger = logger;
        _config = config.Value;
        _backends = backends;
        _dispatcher = dispatcher;
        _clusterManager = clusterManager;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try 
        {
            // Start Cluster
            _clusterManager.Start();

            _logger.LogInformation("Initializing 9P translation backends...");
            foreach (var backend in _backends)
            {
                try
                {
                    var section = _configuration.GetSection($"Server:{backend.Name}");
                    await backend.InitializeAsync(section);
                    _logger.LogInformation("Backend '{BackendName}' initialized at {MountPath}", backend.Name, backend.MountPath);
                    
                    if (_clusterManager.Registry != null)
                    {
                        var backendActor = _clusterManager.System!.ActorOf(Props.Create(() => new Cluster.Actors.BackendSupervisorActor(backend)), $"backend-{backend.Name}");
                        _clusterManager.Registry.Tell(new BackendRegistration(backend.MountPath, backendActor));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize backend '{BackendName}'", backend.Name);
                }
            }

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
                    _ = HandleClientAsync(client, stoppingToken);
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
        public uint MSize { get; set; } = DefaultMSize;
        public bool DotU { get; set; } = false;
        public SemaphoreSlim WriteLock { get; } = new(1, 1);
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        var endPoint = client.Client.RemoteEndPoint;
        _logger.LogInformation("Client connected from {EndPoint}", endPoint);
        
        var session = new ClientSession();

        try
        {
            await using var stream = client.GetStream();
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
                        ArrayPool<byte>.Shared.Return(fullMessageBuffer);
                        break;
                    }
                }

                _logger.LogInformation("Incoming: size={Size}, type={Type}, tag={Tag}", size, type, tag);
                _logger.LogInformation("Raw Hex: {Hex}", BitConverter.ToString(fullMessageBuffer, 0, (int)size));

                // Fire and forget handling of the message to allow pipelining
                _ = Task.Run(async () => {
                    try {
                        var response = await DispatchMessageAsync(fullMessageBuffer.AsMemory(0, (int)size), type, tag, session);
                        
                        if (response is Rversion rv)
                        {
                            session.MSize = rv.MSize;
                            session.DotU = rv.Version == "9P2000.u";
                            _logger.LogInformation("Negotiated MSize: {MSize}, DotU: {DotU}, Version: {Version}", session.MSize, session.DotU, rv.Version);
                        }

                        await SendResponseAsync(stream, response, session.WriteLock);
                    }
                    catch (Exception ex) {
                        _logger.LogError(ex, "Unexpected error during dispatch");
                        // Should probably send Rlerror if .L is negotiated, but standard Rerror for now
                        await SendResponseAsync(stream, new Rerror(tag, ex.Message), session.WriteLock);
                    }
                    finally {
                        ArrayPool<byte>.Shared.Return(fullMessageBuffer);
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
        var result = NinePSharp.Parser.NinePParser.parse(session.DotU, fullMessageBuffer);
        if (result.IsError)
        {
            _logger.LogError("Failed to parse message: {Error}", result.ErrorValue);
            return new Rerror(tag, result.ErrorValue);
        }

        return await _dispatcher.DispatchAsync(result.ResultValue, session.DotU);
    }

    private async Task SendResponseAsync(NetworkStream stream, object response, SemaphoreSlim writeLock)
    {
        if (response is not ISerializable serializable) return;

        byte[] outBuffer = new byte[serializable.Size];
        serializable.WriteTo(outBuffer);

        await writeLock.WaitAsync();
        try {
            _logger.LogInformation("Outgoing: {Hex}", BitConverter.ToString(outBuffer));
            await stream.WriteAsync(outBuffer);
        }
        finally {
            writeLock.Release();
        }
    }
}
