using System;
using System.Security;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using StellarServer = StellarDotnetSdk.Server;

namespace NinePSharp.Server.Backends;

public class StellarBackend : IProtocolBackend
{
    private StellarBackendConfig? _config;
    private StellarServer? _server;
    private readonly ILuxVaultService _vault;

    public StellarBackend(ILuxVaultService vault)
    {
        _vault = vault;
    }

    public string Name => "Stellar";
    public string MountPath => _config?.MountPath ?? "/stellar";

    public Task InitializeAsync(IConfiguration configuration)
    {
        _config = configuration.GetSection("Server:Stellar").Get<StellarBackendConfig>();
        if (_config != null && !string.IsNullOrWhiteSpace(_config.HorizonUrl))
        {
            _server = new StellarServer(_config.HorizonUrl);
        }
        Console.WriteLine($"[Stellar Backend] Initialized with MountPath: {MountPath}");
        return Task.CompletedTask;
    }

    public INinePFileSystem GetFileSystem()
    {
        if (_config == null) throw new InvalidOperationException("Backend not initialized");
        return new StellarFileSystem(_config, _server, _vault);
    }

    public INinePFileSystem GetFileSystem(SecureString? credentials) => GetFileSystem();
}
