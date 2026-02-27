using System.Collections.Generic;
using System.Threading.Tasks;

namespace NinePSharp.Server.Backends.gRPC;

/// <summary>
/// Interface for gRPC service communication.
/// </summary>
public interface IGrpcTransport
{
    /// <summary>Connects to the gRPC host.</summary>
    Task ConnectAsync(string host, int port);
    /// <summary>Calls a gRPC method.</summary>
    Task<byte[]> CallAsync(string service, string method, byte[] payload, IDictionary<string, string> metadata);
}
