using System;
using System.Collections.Generic;

namespace NinePSharp.Server.Configuration.Models;

public class EthereumBackendConfig : BackendConfigBase
{
    public string RpcUrl { get; set; } = "http://localhost:8545";
    public string DefaultAccount { get; set; } = string.Empty;
    
    /// <summary>
    /// List of JSON-RPC methods this backend is allowed to call on the node.
    /// Example: ["eth_getBalance", "eth_sendTransaction", "eth_call"]
    /// </summary>
    public List<string> AllowedMethods { get; set; } = new();

    /// <summary>Key name used to look up RPC credentials from the vault. If set, overrides RpcUrl at runtime.</summary>
    public string VaultKey { get; set; } = string.Empty;
}
