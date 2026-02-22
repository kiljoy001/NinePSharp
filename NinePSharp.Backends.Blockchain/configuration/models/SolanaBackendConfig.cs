namespace NinePSharp.Server.Configuration.Models;

public class SolanaBackendConfig : BackendConfigBase
{
    public string RpcUrl { get; set; } = "https://api.mainnet-beta.solana.com";
}
