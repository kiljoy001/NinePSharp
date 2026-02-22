namespace NinePSharp.Server.Configuration.Models;

public class BitcoinBackendConfig : BackendConfigBase
{
    public string Network { get; set; } = "Main"; // Main, TestNet, RegTest
    public string? RpcUrl { get; set; }
    public string? RpcUser { get; set; }
    public string? RpcPassword { get; set; }
}
