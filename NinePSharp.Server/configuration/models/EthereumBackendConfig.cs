using System;

namespace NinePSharp.Server.Configuration.Models;

public class EthereumBackendConfig : BackendConfigBase
{
    public string RpcUrl { get; set; } = "http://localhost:8545";
    public string DefaultAccount { get; set; } = string.Empty;
    public string PrivateKey { get; set; } = string.Empty;
}
