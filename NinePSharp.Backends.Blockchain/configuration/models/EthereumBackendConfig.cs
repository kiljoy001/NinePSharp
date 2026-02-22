using System;

namespace NinePSharp.Server.Configuration.Models;

public class EthereumBackendConfig : BackendConfigBase
{
    public string RpcUrl { get; set; } = "http://localhost:8545";
    public string DefaultAccount { get; set; } = string.Empty;
    public string PrivateKey { get; set; } = string.Empty;
    /// <summary>Key name used to look up RPC credentials from the vault. If set, overrides RpcUrl at runtime.</summary>
    public string VaultKey { get; set; } = string.Empty;
}
