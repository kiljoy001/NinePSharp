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
        _config = configuration.Get<DatabaseBackendConfig>();
        Console.WriteLine($"[Database Backend] Initialized with MountPath: {MountPath}");
        return Task.CompletedTask;
    }

    public INinePFileSystem GetFileSystem()
    {
        if (_config == null) throw new InvalidOperationException("Backend not initialized");
        return new DatabaseFileSystem(_config, _vault);
    }

    public INinePFileSystem GetFileSystem(SecureString? credentials)
    {
        if (_config == null) throw new InvalidOperationException("Backend not initialized");
        // For simplicity, we don't currently use the SecureString credentials here, 
        // but we've updated the signature for compatibility.
        return GetFileSystem();
    }
}
