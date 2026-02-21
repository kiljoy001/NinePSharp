using System;
using System.Net;
using System.Runtime.InteropServices;
using System.Security;
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

    public INinePFileSystem GetFileSystem(SecureString? credentials)
    {
        if (_config == null) throw new InvalidOperationException("Backend not initialized");

        // Credential resolution: client-supplied → vault → config
        string? rpcUrl = SecureStringToString(credentials);

        if (rpcUrl == null && !string.IsNullOrEmpty(_config.VaultKey))
        {
            // Look up credentials stored in the vault under the configured key
            var seed = _vault.DeriveSeed(_config.VaultKey, System.Text.Encoding.UTF8.GetBytes(_config.VaultKey));
            var hiddenId = _vault.GenerateHiddenId(seed);
            var vaultFile = $"secret_{hiddenId}.vlt";
            if (System.IO.File.Exists(vaultFile))
            {
                var raw = System.IO.File.ReadAllBytes(vaultFile);
                rpcUrl = _vault.Decrypt(raw, _config.VaultKey);
            }
        }

        IWeb3 web3 = rpcUrl != null
            ? new Web3(rpcUrl)
            : (_web3 ?? throw new InvalidOperationException("Backend not initialized"));

        return new EthereumFileSystem(_config, web3, _vault);
    }
}
