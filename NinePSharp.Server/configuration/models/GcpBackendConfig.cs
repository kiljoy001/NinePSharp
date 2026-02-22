namespace NinePSharp.Server.Configuration.Models;

public class GcpBackendConfig
{
    public string Name { get; set; } = "GCP";
    public string MountPath { get; set; } = "/gcp";
    
    public string? ProjectId { get; set; }

    // Vault key to look up ServiceAccountJson
    public string? VaultKey { get; set; }
}
