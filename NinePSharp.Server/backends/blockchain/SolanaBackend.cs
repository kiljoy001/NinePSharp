using System;
using System.Security;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Server.Backends;

public class SolanaBackend : IProtocolBackend
{
    private SolanaBackendConfig? _config;
    private readonly ILuxVaultService _vault;

    public SolanaBackend(ILuxVaultService vault)
    {
        _vault = vault;
    }

    public string Name => "Solana";
    public string MountPath => _config?.MountPath ?? "/sol";

    public Task InitializeAsync(IConfiguration configuration)
    {
        _config = configuration.GetSection("Server:Solana").Get<SolanaBackendConfig>();
        Console.WriteLine($"[Solana Backend] Initialized with MountPath: {MountPath}");
        return Task.CompletedTask;
    }

    public INinePFileSystem GetFileSystem()
    {
        if (_config == null) throw new InvalidOperationException("Backend not initialized");
        return new SolanaFileSystem(_config, null, _vault);
    }

    public INinePFileSystem GetFileSystem(SecureString? credentials) => GetFileSystem();
}
