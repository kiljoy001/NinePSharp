using System;
using System.Data;
using System.Data.Common;
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
        
        // Use DbProviderFactories to get a generic connection
        // Note: The caller might need to register factories in Program.cs
        return new DatabaseFileSystem(_config ?? new DatabaseBackendConfig(), _vault);
    }
}
