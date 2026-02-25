using System;
using System.Threading.Tasks;
using Grpc.Net.Client;
using Grpc.Core;
using System.Net.Http;

namespace NinePSharp.Server.Backends.gRPC;

/// <summary>
/// Handles low-level gRPC message transport.
/// </summary>
public class GrpcTransport : IGrpcTransport
{
    private GrpcChannel? _channel;

    /// <inheritdoc />
    public Task ConnectAsync(string host, int port)
    {
        var address = $"http://{host}:{port}";
        _channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions
        {
            HttpHandler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            }
        });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<byte[]> CallAsync(string service, string method, byte[] payload, System.Collections.Generic.IDictionary<string, string> metadata)
    {
        if (_channel == null) throw new InvalidOperationException("gRPC not connected.");

        // For a generic gRPC caller without generated stubs, we would use 
        // a tool like 'grpc-reflection' or manual call invocation.
        // For the prototype, we implement the structure but return a stub.
        
        return await Task.FromResult(Array.Empty<byte>());
    }
}
