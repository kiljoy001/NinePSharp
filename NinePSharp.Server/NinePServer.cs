using System;
using System.Buffers.Binary;
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
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Interfaces;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Server;

public class NinePServer : BackgroundService
{
    private readonly ILogger<NinePServer> _logger;
    private readonly ServerConfig _config;
    private readonly IEnumerable<IProtocolBackend> _backends;
    private readonly IConfiguration _configuration;
    private readonly NinePFSDispatcher _dispatcher;

    public NinePServer(
        ILogger<NinePServer> logger, 
        IOptions<ServerConfig> config, 
        IEnumerable<IProtocolBackend> backends,
        IConfiguration configuration,
        NinePFSDispatcher dispatcher)
    {
        _logger = logger;
        _config = config.Value;
        _backends = backends;
        _configuration = configuration;
        _dispatcher = dispatcher;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Initializing 9P translation backends...");
        foreach (var backend in _backends)
        {
            try
            {
                // Find the configuration section for this backend
                // This assumes the backend name matches the section name in config.json
                var section = _configuration.GetSection($"Server:{backend.Name}");
                await backend.InitializeAsync(section);
                _logger.LogInformation("Backend '{BackendName}' initialized at {MountPath}", backend.Name, backend.MountPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize backend '{BackendName}'", backend.Name);
            }
        }

        // For now, only the first endpoint is handled
        var endpoint = _config.Endpoints.Count > 0 
            ? _config.Endpoints[0] 
            : null;

        if (endpoint == null)
        {
            _logger.LogWarning("No endpoints configured for 9P server.");
            return;
        }

        var listener = new TcpListener(IPAddress.Parse(endpoint.Address), endpoint.Port);
        listener.Start();
        _logger.LogInformation("9P Server listening on {Address}:{Port} ({Protocol})...", endpoint.Address, endpoint.Port, endpoint.Protocol);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var client = await listener.AcceptTcpClientAsync(stoppingToken);
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
        
        listener.Stop();
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        var endPoint = client.Client.RemoteEndPoint;
        _logger.LogInformation("Client connected from {EndPoint}", endPoint);

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
                var type = (MessageTypes)headerBuffer[4];
                ushort tag = BinaryPrimitives.ReadUInt16LittleEndian(headerBuffer.AsSpan(5, 2));

                _logger.LogDebug("Received: Type={Type}, Tag={Tag}, Size={Size}", type, tag, size);

                uint payloadSize = size - NinePConstants.HeaderSize;
                var fullMessageBuffer = new byte[size];
                headerBuffer.CopyTo(fullMessageBuffer, 0);

                if (payloadSize > 0)
                {
                    int payloadRead = await stream.ReadAtLeastAsync(fullMessageBuffer.AsMemory((int)NinePConstants.HeaderSize, (int)payloadSize), (int)payloadSize, throwOnEndOfStream: false, ct);
                    if (payloadRead < payloadSize) break;
                }

                await DispatchMessageAsync(fullMessageBuffer.AsMemory(), stream, type, tag);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling client {EndPoint}", endPoint);
        }
        finally
        {
            client.Close();
        }
    }

    private async Task DispatchMessageAsync(ReadOnlyMemory<byte> fullMessageBuffer, NetworkStream stream, MessageTypes type, ushort tag)
    {
        // Use the FSharp message parser to get a NinePMessage
        var result = NinePSharp.Parser.NinePParser.parse(false, fullMessageBuffer.ToArray());
        if (result.IsError)
        {
            _logger.LogError("Failed to parse message: {Error}", result.ErrorValue);
            return;
        }

        var message = result.ResultValue;
        var response = await _dispatcher.DispatchAsync(message);

        // Standard 9P negotiation reply special casing for R-messages
        byte[] outBuffer;
        if (response is Rversion rversion)
        {
             outBuffer = new byte[rversion.Size];
             rversion.WriteTo(outBuffer.AsSpan());
        }
        else if (response is Rattach rattach)
        {
             outBuffer = new byte[rattach.Size];
             rattach.WriteTo(outBuffer.AsSpan());
        }
        else if (response is Rerror rerror)
        {
             outBuffer = new byte[rerror.Size];
             rerror.WriteTo(outBuffer.AsSpan());
        }
        else if (response is Rwalk rwalk)
        {
             outBuffer = new byte[rwalk.Size];
             rwalk.WriteTo(outBuffer.AsSpan());
        }
        else if (response is ISerializable serializable)
        {
            // Generic serialization for other messages that implement ISerializable
            outBuffer = new byte[serializable.Size];
            serializable.WriteTo(outBuffer.AsSpan());
        }
        else
        {
            _logger.LogWarning("Response for {Type} does not implement ISerializable or is not a handled R-message.", response.GetType().Name);
            return;
        }

        await stream.WriteAsync(outBuffer);
    }
}
