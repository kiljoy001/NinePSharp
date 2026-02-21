using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using CardanoSharp.Wallet;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Server.Backends;

public class CardanoBackend : IProtocolBackend
{
    private CardanoBackendConfig? _config;
    private readonly ILuxVaultService _vault;

    public CardanoBackend(ILuxVaultService vault)
    {
        _vault = vault;
    }

    public string Name => "Cardano";
    public string MountPath => _config?.MountPath ?? "/cardano";

    public Task InitializeAsync(IConfiguration configuration)
    {
        _config = configuration.GetSection("Server:Cardano").Get<CardanoBackendConfig>();
        Console.WriteLine($"[Cardano Backend] Initialized with MountPath: {MountPath}");
        return Task.CompletedTask;
    }

    public INinePFileSystem GetFileSystem()
    {
        if (_config == null) throw new InvalidOperationException("Backend not initialized");
        return new CardanoFileSystem(_config ?? new CardanoBackendConfig(), _vault);
    }
}
