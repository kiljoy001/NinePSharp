using System.Security.Cryptography.X509Certificates;
using System;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Google.Cloud.Storage.V1;
using Google.Cloud.SecretManager.V1;
using Microsoft.Extensions.Configuration;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Backends.Cloud;

public class GcpBackend : IProtocolBackend
{
    private GcpBackendConfig? _config;
    private readonly ILuxVaultService _vault;
    private StorageClient? _storageClient;
    private SecretManagerServiceClient? _secretsClient;

    public GcpBackend(ILuxVaultService vault)
    {
        _vault = vault;
    }

    public string Name => "GCP";
    public string MountPath => _config?.MountPath ?? "/gcp";

    public async Task InitializeAsync(IConfiguration configuration)
    {
        try {
            _config = configuration.Get<GcpBackendConfig>();
            if (_config != null)
            {
                (_storageClient, _secretsClient) = CreateClients(null);
                Console.WriteLine($"[GCP Backend] Initialized with MountPath: {MountPath}");
            }
            else {
                Console.WriteLine("[GCP Backend] No configuration found, skipping initialization.");
            }
        }
        catch (Exception ex) {
            Console.WriteLine($"[GCP Backend] Failed to initialize: {ex.Message}");
        }
    }

    public INinePFileSystem GetFileSystem(X509Certificate2? certificate = null) => new GcpCloudFileSystem(_config!, _storageClient!, _secretsClient!, _vault);

    public INinePFileSystem GetFileSystem(SecureString? credentials, X509Certificate2? certificate = null)
    {
        var (storage, secrets) = CreateClients(credentials);
        return new GcpCloudFileSystem(_config!, storage, secrets, _vault);
    }

    private (StorageClient?, SecretManagerServiceClient?) CreateClients(SecureString? credentials)
    {
        // For GCP, we usually need a Service Account JSON.
        // If provided via 9P Auth, it's expected to be the full JSON string.
        string? json = null;
        if (credentials != null) json = SecureStringHelper.ToString(credentials);
        else if (!string.IsNullOrEmpty(_config?.VaultKey))
        {
            var seed = _vault.DeriveSeed(_config.VaultKey, Encoding.UTF8.GetBytes(_config.VaultKey));
            var hiddenId = _vault.GenerateHiddenId(seed);
            var vaultFile = _vault.GetVaultPath($"gcp_creds_{hiddenId}.vlt");
            if (System.IO.File.Exists(vaultFile))
            {
                var encrypted = System.IO.File.ReadAllBytes(vaultFile);
                var raw = _vault.DecryptToBytes(encrypted, _config.VaultKey);
                if (raw != null)
                {
                    using (raw)
                    {
                        json = Encoding.UTF8.GetString(raw.Span);
                    }
                }
            }
        }

        StorageClient? storage = null;
        SecretManagerServiceClient? secrets = null;

        if (json != null)
        {
            #pragma warning disable CS0618 // Type or member is obsolete
            var credential = Google.Apis.Auth.OAuth2.GoogleCredential.FromJson(json);
            #pragma warning restore CS0618

            storage = new StorageClientBuilder { Credential = credential }.Build();
            secrets = new SecretManagerServiceClientBuilder { Credential = credential }.Build();
        }
        else
        {
            storage = StorageClient.Create();
            secrets = SecretManagerServiceClient.Create();
        }

        return (storage, secrets);
    }
}
