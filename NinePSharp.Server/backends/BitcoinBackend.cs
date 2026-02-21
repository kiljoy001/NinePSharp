using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NBitcoin;
using NBitcoin.RPC;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Server.Backends;

public class BitcoinBackend : IProtocolBackend
{
    private BitcoinBackendConfig? _config;
    private RPCClient? _rpcClient;
    private readonly ILuxVaultService _vault;

    public BitcoinBackend(ILuxVaultService vault)
    {
        _vault = vault;
    }

    public string Name => "Bitcoin";
    public string MountPath => _config?.MountPath ?? "/btc";

    public Task InitializeAsync(IConfiguration configuration)
    {
        _config = configuration.GetSection("Server:Bitcoin").Get<BitcoinBackendConfig>();
        if (_config != null && !string.IsNullOrEmpty(_config.RpcUrl))
        {
            var network = _config.Network.ToLower() switch
            {
                "testnet" => Network.TestNet,
                "regtest" => Network.RegTest,
                _ => Network.Main
            };
            
            RPCCredentialString? creds = null;
            if (!string.IsNullOrEmpty(_config.RpcUser) && !string.IsNullOrEmpty(_config.RpcPassword))
            {
                creds = RPCCredentialString.Parse($"{_config.RpcUser}:{_config.RpcPassword}");
            }

            _rpcClient = new RPCClient(creds, _config.RpcUrl, network);
        }
        Console.WriteLine($"[Bitcoin Backend] Initialized with MountPath: {MountPath}");
        return Task.CompletedTask;
    }
    public INinePFileSystem GetFileSystem()
    {
        if (_config == null) throw new InvalidOperationException("Backend not initialized");
        return new BitcoinFileSystem(_config ?? new BitcoinBackendConfig(), _rpcClient, _vault);
    }
}
