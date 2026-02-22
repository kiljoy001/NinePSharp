namespace NinePSharp.Server.Configuration.Models;

public class AzureBackendConfig
{
    public string Name { get; set; } = "Azure";
    public string MountPath { get; set; } = "/azure";
    
    // For Blob Storage
    public string? BlobServiceUri { get; set; }
    
    // For Key Vault
    public string? KeyVaultUri { get; set; }

    // Vault key to look up TenantId:ClientId:ClientSecret
    public string? VaultKey { get; set; }
}
