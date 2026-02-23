using System.Collections.Generic;

namespace NinePSharp.Server.Configuration.Models;

public class BitcoinBackendConfig : BackendConfigBase
{
    public string Network { get; set; } = "Main"; // Main, TestNet, RegTest
    public string? RpcUrl { get; set; }
    public string? RpcUser { get; set; }
    public string? RpcPassword { get; set; }

    /// <summary>
    /// List of JSON-RPC methods this backend is allowed to call on the node.
    /// Example: ["getbalance", "sendtoaddress", "getnewaddress"]
    /// </summary>
    public List<string> AllowedMethods { get; set; } = new();
}
