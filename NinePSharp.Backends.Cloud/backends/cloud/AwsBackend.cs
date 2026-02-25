using System.Security.Cryptography.X509Certificates;
using System;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.SecretsManager;
using Microsoft.Extensions.Configuration;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Backends.Cloud;

/// <summary>
/// Backend implementation for Amazon Web Services (S3 and Secrets Manager).
/// </summary>
public class AwsBackend : IProtocolBackend
{
    private AwsBackendConfig? _config;
    private readonly ILuxVaultService _vault;
    private IAmazonS3? _s3Client;
    private IAmazonSecretsManager? _secretsClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="AwsBackend"/> class.
    /// </summary>
    /// <param name="vault">The vault service.</param>
    public AwsBackend(ILuxVaultService vault)
    {
        _vault = vault;
    }

    /// <inheritdoc />
    public string Name => "AWS";
    /// <inheritdoc />
    public string MountPath => _config?.MountPath ?? "/aws";

    /// <inheritdoc />
    public async Task InitializeAsync(IConfiguration configuration)
    {
        try {
            _config = configuration.Get<AwsBackendConfig>();
            if (_config != null)
            {
                _s3Client = CreateS3Client(null);
                _secretsClient = CreateSecretsClient(null);
                Console.WriteLine($"[AWS Backend] Initialized with MountPath: {MountPath}");
            }
            else {
                Console.WriteLine("[AWS Backend] No configuration found, skipping initialization.");
            }
        }
        catch (Exception ex) {
            Console.WriteLine($"[AWS Backend] Failed to initialize: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public INinePFileSystem GetFileSystem(X509Certificate2? certificate = null) => new AwsCloudFileSystem(_config!, _s3Client!, _secretsClient!, _vault);

    /// <inheritdoc />
    public INinePFileSystem GetFileSystem(SecureString? credentials, X509Certificate2? certificate = null)
    {
        var s3 = CreateS3Client(credentials);
        var secrets = CreateSecretsClient(credentials);
        return new AwsCloudFileSystem(_config!, s3, secrets, _vault);
    }

    private IAmazonS3 CreateS3Client(SecureString? credentials)
    {
        var (awsCreds, config) = GetAwsSetup<AmazonS3Config>(credentials);
        return awsCreds != null ? new AmazonS3Client(awsCreds, config) : new AmazonS3Client(config);
    }

    private IAmazonSecretsManager CreateSecretsClient(SecureString? credentials)
    {
        var (awsCreds, config) = GetAwsSetup<AmazonSecretsManagerConfig>(credentials);
        return awsCreds != null ? new AmazonSecretsManagerClient(awsCreds, config) : new AmazonSecretsManagerClient(config);
    }

    private (AWSCredentials?, T) GetAwsSetup<T>(SecureString? credentials) where T : ClientConfig, new()
    {
        AWSCredentials? awsCreds = null;
        
        if (credentials != null)
        {
            string credsStr = SecureStringHelper.ToString(credentials);
            var parts = credsStr.Split(':', 2);
            if (parts.Length == 2) awsCreds = new BasicAWSCredentials(parts[0], parts[1]);
        }
        else if (!string.IsNullOrEmpty(_config?.VaultKey))
        {
            using var seed = new SecureBuffer(32, _vault.GetLocalArena());
            _vault.DeriveSeed(_config.VaultKey, Encoding.UTF8.GetBytes(_config.VaultKey), seed.Span);
            var hiddenId = _vault.GenerateHiddenId(seed.Span);
            var vaultFile = _vault.GetVaultPath($"aws_creds_{hiddenId}.vlt");
            
            if (System.IO.File.Exists(vaultFile))
            {
                var encrypted = System.IO.File.ReadAllBytes(vaultFile);
                var rawBytes = _vault.DecryptToBytes(encrypted, _config.VaultKey);
                if (rawBytes != null)
                {
                    using (rawBytes)
                    {
                        string credsStr = Encoding.UTF8.GetString(rawBytes.Span);
                        var parts = credsStr.Split(':', 2);
                        if (parts.Length == 2) awsCreds = new BasicAWSCredentials(parts[0], parts[1]);
                    }
                }
            }
        }

        var config = new T();
        if (!string.IsNullOrEmpty(_config?.Region)) config.RegionEndpoint = RegionEndpoint.GetBySystemName(_config.Region);
        if (!string.IsNullOrEmpty(_config?.ServiceUrl)) config.ServiceURL = _config.ServiceUrl;

        return (awsCreds, config);
    }
}
