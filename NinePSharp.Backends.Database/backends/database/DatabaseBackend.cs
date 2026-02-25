using System.Security.Cryptography.X509Certificates;
using System;
using System.Security;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Server.Backends;

/// <summary>
/// Backend implementation for database systems (SQL/NoSQL).
/// </summary>
public class DatabaseBackend : IProtocolBackend
{
    private DatabaseBackendConfig? _config;
    private readonly ILuxVaultService _vault;

    /// <summary>
    /// Initializes a new instance of the <see cref="DatabaseBackend"/> class.
    /// </summary>
    /// <param name="vault">The vault service.</param>
    public DatabaseBackend(ILuxVaultService vault)
    {
        _vault = vault;
    }
    
    /// <inheritdoc />
    public string Name => "Database";
    /// <inheritdoc />
    public string MountPath => _config?.MountPath ?? "/db";

    /// <inheritdoc />
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

    /// <inheritdoc />
    public INinePFileSystem GetFileSystem(X509Certificate2? certificate = null) => GetFileSystem(null);

    /// <inheritdoc />
    public INinePFileSystem GetFileSystem(SecureString? credentials, X509Certificate2? certificate = null)
    {
        if (_config == null) throw new InvalidOperationException("Backend not initialized");
        return new DatabaseFileSystem(_config, _vault, credentials);
    }
}
