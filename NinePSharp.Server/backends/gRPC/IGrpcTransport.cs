using System.Collections.Generic;
using System.Threading.Tasks;

namespace NinePSharp.Server.Backends.gRPC;

public interface IGrpcTransport
{
    Task ConnectAsync(string host, int port);
    Task<byte[]> CallAsync(string service, string method, byte[] payload, IDictionary<string, string> metadata);
}
