using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Nethereum.Web3;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Server.Backends;

public class EthereumBackend : IProtocolBackend
{
    private EthereumBackendConfig? _config;
    private IWeb3? _web3;
    private readonly ILuxVaultService _vault;

    public EthereumBackend(ILuxVaultService vault)
    {
        _vault = vault;
    }

    public string Name => "Ethereum";
    public string MountPath => _config?.MountPath ?? "/eth";

    public Task InitializeAsync(IConfiguration configuration)
    {
        _config = configuration.Get<EthereumBackendConfig>();
        if (_config != null && !string.IsNullOrEmpty(_config.RpcUrl))
        {
            _web3 = new Web3(_config.RpcUrl);
        }
        Console.WriteLine($"[Ethereum Backend] Initialized with MountPath: {MountPath}");
        return Task.CompletedTask;
    }

    public INinePFileSystem GetFileSystem()
    {
        if (_config == null || _web3 == null) throw new InvalidOperationException("Backend not initialized");
        return new EthereumFileSystem(_config, _web3, _vault);
    }
}
