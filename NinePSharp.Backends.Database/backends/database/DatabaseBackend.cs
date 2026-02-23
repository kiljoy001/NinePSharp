using System.Security.Cryptography.X509Certificates;
using System;
using System.Security;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Server.Backends;

public class DatabaseBackend : IProtocolBackend
{
    private DatabaseBackendConfig? _config;
    private readonly ILuxVaultService _vault;

    public DatabaseBackend(ILuxVaultService vault)
    {
        _vault = vault;
    }
    
    public string Name => "Database";
    public string MountPath => _config?.MountPath ?? "/db";

    public Task InitializeAsync(IConfiguration configuration)
    {
        try {
            _config = configuration.Get<DatabaseBackendConfig>();
            Console.WriteLine($"[Database Backend] Initialized with MountPath: {MountPath}");
        }
        catch (Exception ex) {
            Console.WriteLine($"[Database Backend] Failed to initialize: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    public INinePFileSystem GetFileSystem(X509Certificate2? certificate = null) => GetFileSystem(null);

    public INinePFileSystem GetFileSystem(SecureString? credentials, X509Certificate2? certificate = null)
    {
        if (_config == null) throw new InvalidOperationException("Backend not initialized");
        return new DatabaseFileSystem(_config, _vault, credentials);
    }
}
