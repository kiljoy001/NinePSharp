using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Solnet.Rpc;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Server.Backends;

public class SolanaBackend : IProtocolBackend
{
    private SolanaBackendConfig? _config;
    private IRpcClient? _rpcClient;
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
        if (_config != null && !string.IsNullOrEmpty(_config.RpcUrl))
        {
            _rpcClient = ClientFactory.GetClient(_config.RpcUrl);
        }
        Console.WriteLine($"[Solana Backend] Initialized with MountPath: {MountPath}");
        return Task.CompletedTask;
    }

    public INinePFileSystem GetFileSystem()
    {
        if (_config == null) throw new InvalidOperationException("Backend not initialized");
        return new SolanaFileSystem(_config, _rpcClient, _vault);
    }
}
