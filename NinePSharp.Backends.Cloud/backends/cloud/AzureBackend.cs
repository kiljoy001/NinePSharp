using System.Security.Cryptography.X509Certificates;
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

/// <summary>
/// Backend implementation for Microsoft Azure (Blobs and Key Vault).
/// </summary>
public class AzureBackend : IProtocolBackend
{
    private AzureBackendConfig? _config;
    private readonly ILuxVaultService _vault;
    private BlobServiceClient? _blobClient;
    private SecretClient? _secretClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureBackend"/> class.
    /// </summary>
    /// <param name="vault">The vault service.</param>
    public AzureBackend(ILuxVaultService vault)
    {
        _vault = vault;
    }

    /// <inheritdoc />
    public string Name => "Azure";
    /// <inheritdoc />
    public string MountPath => _config?.MountPath ?? "/azure";

    /// <inheritdoc />
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

    /// <inheritdoc />
    public INinePFileSystem GetFileSystem(X509Certificate2? certificate = null) => new AzureCloudFileSystem(_config!, _blobClient!, _secretClient!, _vault);

    /// <inheritdoc />
    public INinePFileSystem GetFileSystem(SecureString? credentials, X509Certificate2? certificate = null)
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
            SecureStringHelper.Use(credentials, span => {
                int firstColon = span.IndexOf((byte)':');
                int lastColon = span.LastIndexOf((byte)':');
                if (firstColon != -1 && lastColon != -1 && firstColon != lastColon)
                {
                    var tenantId = Encoding.UTF8.GetString(span.Slice(0, firstColon));
                    var clientId = Encoding.UTF8.GetString(span.Slice(firstColon + 1, lastColon - firstColon - 1));
                    var clientSecret = Encoding.UTF8.GetString(span.Slice(lastColon + 1));
                    cred = new ClientSecretCredential(tenantId, clientId, clientSecret);
                }
            });
        }
        else if (!string.IsNullOrEmpty(_config?.VaultKey))
        {
            using var seed = new SecureBuffer(32, _vault.GetLocalArena());
            _vault.DeriveSeed(_config.VaultKey, Encoding.UTF8.GetBytes(_config.VaultKey), seed.Span);
            var hiddenId = _vault.GenerateHiddenId(seed.Span);
            var vaultFile = _vault.GetVaultPath($"az_creds_{hiddenId}.vlt");
            
            if (System.IO.File.Exists(vaultFile))
            {
                var encrypted = System.IO.File.ReadAllBytes(vaultFile);
                var rawBytes = _vault.DecryptToBytes(encrypted, _config.VaultKey);
                if (rawBytes != null)
                {
                    using (rawBytes)
                    {
                        var span = rawBytes.Span;
                        int firstColon = span.IndexOf((byte)':');
                        int lastColon = span.LastIndexOf((byte)':');
                        if (firstColon != -1 && lastColon != -1 && firstColon != lastColon)
                        {
                            var tenantId = Encoding.UTF8.GetString(span.Slice(0, firstColon));
                            var clientId = Encoding.UTF8.GetString(span.Slice(firstColon + 1, lastColon - firstColon - 1));
                            var clientSecret = Encoding.UTF8.GetString(span.Slice(lastColon + 1));
                            cred = new ClientSecretCredential(tenantId, clientId, clientSecret);
                        }
                    }
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
