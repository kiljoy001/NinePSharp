using System.Collections.Generic;

namespace NinePSharp.Server.Configuration.Models;

public class SolanaBackendConfig : BackendConfigBase
{
    public string RpcUrl { get; set; } = "https://api.mainnet-beta.solana.com";

    /// <summary>
    /// List of JSON-RPC methods this backend is allowed to call on the node.
    /// Example: ["getBalance", "sendTransaction"]
    /// </summary>
    public List<string> AllowedMethods { get; set; } = new();
}
