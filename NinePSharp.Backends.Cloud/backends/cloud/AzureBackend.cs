using System;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Backends.Cloud;

public class AzureBackend : IProtocolBackend
{
    private AzureBackendConfig? _config;
    private readonly ILuxVaultService _vault;
    private BlobServiceClient? _blobClient;
    private SecretClient? _secretClient;

    public AzureBackend(ILuxVaultService vault)
    {
        _vault = vault;
    }

    public string Name => "Azure";
    public string MountPath => _config?.MountPath ?? "/azure";

    public async Task InitializeAsync(IConfiguration configuration)
    {
        try {
            _config = configuration.Get<AzureBackendConfig>();
            if (_config != null)
            {
                (_blobClient, _secretClient) = CreateClients(null);
                Console.WriteLine($"[Azure Backend] Initialized with MountPath: {MountPath}");
            }
            else {
                Console.WriteLine("[Azure Backend] No configuration found, skipping initialization.");
            }
        }
        catch (Exception ex) {
            Console.WriteLine($"[Azure Backend] Failed to initialize: {ex.Message}");
        }
    }

    public INinePFileSystem GetFileSystem() => new AzureCloudFileSystem(_config!, _blobClient!, _secretClient!, _vault);

    public INinePFileSystem GetFileSystem(SecureString? credentials)
    {
        var (blobs, secrets) = CreateClients(credentials);
        return new AzureCloudFileSystem(_config!, blobs, secrets, _vault);
    }

    private (BlobServiceClient?, SecretClient?) CreateClients(SecureString? credentials)
    {
        TokenCredential? cred = null;

        if (credentials != null)
        {
            // Format: "TenantId:ClientId:ClientSecret"
            string credsStr = SecureStringHelper.ToString(credentials);
            var parts = credsStr.Split(':', 3);
            if (parts.Length == 3) cred = new ClientSecretCredential(parts[0], parts[1], parts[2]);
        }
        else if (!string.IsNullOrEmpty(_config?.VaultKey))
        {
            var seed = _vault.DeriveSeed(_config.VaultKey, Encoding.UTF8.GetBytes(_config.VaultKey));
            var hiddenId = _vault.GenerateHiddenId(seed);
            var vaultFile = _vault.GetVaultPath($"az_creds_{hiddenId}.vlt");
            
            if (System.IO.File.Exists(vaultFile))
            {
                var encrypted = System.IO.File.ReadAllBytes(vaultFile);
                var rawBytes = _vault.DecryptToBytes(encrypted, _config.VaultKey);
                if (rawBytes != null)
                {
                    try {
                        string credsStr = Encoding.UTF8.GetString(rawBytes);
                        var parts = credsStr.Split(':', 3);
                        if (parts.Length == 3) cred = new ClientSecretCredential(parts[0], parts[1], parts[2]);
                    }
                    finally { Array.Clear(rawBytes); }
                }
            }
        }

        if (cred == null) cred = new DefaultAzureCredential();

        BlobServiceClient? b = !string.IsNullOrEmpty(_config?.BlobServiceUri) 
            ? new BlobServiceClient(new Uri(_config.BlobServiceUri), cred) 
            : null;
            
        SecretClient? s = !string.IsNullOrEmpty(_config?.KeyVaultUri) 
            ? new SecretClient(new Uri(_config.KeyVaultUri), cred) 
            : null;

        return (b, s);
    }
}
