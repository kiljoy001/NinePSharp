using System.Collections.Generic;

namespace NinePSharp.Server.Configuration.Models;

/// <summary>
/// Maps a 9P filename to an explicit JSON-RPC method call.
/// No methods can be called unless they are declared here.
/// </summary>
public class JsonRpcEndpointConfig
{
    /// <summary>The filename exposed under the backend's mount path, e.g. "balance"</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional hierarchical path, e.g. "accounts/eth". If empty, endpoint is in root.
    /// This allows mapping "balance" under "/rpc/accounts/eth/balance".
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>The exact JSON-RPC method to call, e.g. "getbalance"</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// Optional static params to send with every call (e.g. [] for getinfo).
    /// For parameterised endpoints leave empty; params come from the write payload.
    /// </summary>
    public List<string> Params { get; set; } = new();

    /// <summary>If true, the client can write a param to override / supply runtime params.</summary>
    public bool Writable { get; set; } = false;

    /// <summary>Human-readable description shown in stat.</summary>
    public string Description { get; set; } = string.Empty;
}

public class JsonRpcBackendConfig : BackendConfigBase
{
    /// <summary>Full URL of the JSON-RPC endpoint, e.g. http://127.0.0.1:6662/</summary>
    public string EndpointUrl { get; set; } = string.Empty;

    /// <summary>RPC username for basic auth. Can be stored in vault via VaultKey.</summary>
    public string RpcUser { get; set; } = string.Empty;

    /// <summary>RPC password for basic auth. Can be stored in vault via VaultKey.</summary>
    public string RpcPassword { get; set; } = string.Empty;

    /// <summary>Optional vault key to resolve RpcUser:RpcPassword from the vault.</summary>
    public string VaultKey { get; set; } = string.Empty;

    /// <summary>
    /// Explicitly declared endpoints. ONLY these are accessible via 9P.
    /// No endpoint = no access. Never auto-discovered.
    /// </summary>
    public List<JsonRpcEndpointConfig> Endpoints { get; set; } = new();
}
