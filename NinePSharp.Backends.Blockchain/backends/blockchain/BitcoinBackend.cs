using System;
using System.Net;
using System.Runtime.InteropServices;
using System.Security;
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

    private string? SecureStringToString(SecureString? ss)
    {
        if (ss == null) return null;
        IntPtr ptr = Marshal.SecureStringToGlobalAllocUnicode(ss);
        try
        {
            return Marshal.PtrToStringUni(ptr);
        }
        finally
        {
            Marshal.ZeroFreeGlobalAllocUnicode(ptr);
        }
    }

    public INinePFileSystem GetFileSystem()
    {
        if (_config == null) throw new InvalidOperationException("Backend not initialized");
        return new BitcoinFileSystem(_config, _rpcClient, _vault);
    }

    public INinePFileSystem GetFileSystem(SecureString? credentials)
    {
        if (_config == null) throw new InvalidOperationException("Backend not initialized");
        string? credsStr = SecureStringToString(credentials);
        if (credsStr != null)
        {
            var parts = credsStr.Split(':', 2);
            var network = _config.Network.ToLower() switch
            {
                "testnet" => NBitcoin.Network.TestNet,
                "regtest" => NBitcoin.Network.RegTest,
                _ => NBitcoin.Network.Main
            };
            var creds = parts.Length == 2
                ? NBitcoin.RPC.RPCCredentialString.Parse(credsStr)
                : null;
            var rpc = new NBitcoin.RPC.RPCClient(creds, _config.RpcUrl, network);
            return new BitcoinFileSystem(_config, rpc, _vault);
        }
        return GetFileSystem();
    }
}
