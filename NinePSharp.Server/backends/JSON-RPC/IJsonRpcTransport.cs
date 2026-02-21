using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace NinePSharp.Server.Backends.JsonRpc;

/// <summary>
/// Abstraction over a JSON-RPC transport. Allows test injection of fake transports.
/// </summary>
public interface IJsonRpcTransport
{
    Task<JsonNode?> CallAsync(string method, object?[]? args = null);
}
