namespace NinePSharp.Server.Configuration.Models;

public class CardanoBackendConfig : BackendConfigBase
{
    public string Network { get; set; } = "Mainnet";
    public string? BlockfrostProjectId { get; set; }
    public string? BlockfrostApiUrl { get; set; }
}
