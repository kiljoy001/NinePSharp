namespace NinePSharp.Server.Configuration.Models;

public class AwsBackendConfig
{
    public string Name { get; set; } = "AWS";
    public string MountPath { get; set; } = "/aws";
    public string? Region { get; set; }
    public string? ServiceUrl { get; set; }
    
    // Vault key to look up AccessKey:SecretKey
    public string? VaultKey { get; set; }
}
