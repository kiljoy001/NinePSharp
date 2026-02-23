using System;
using System.Net;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Nethereum.Web3;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

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
        try {
            _config = configuration.GetSection("Server:Ethereum").Get<EthereumBackendConfig>();
            if (_config != null && !string.IsNullOrEmpty(_config.RpcUrl))
            {
                _web3 = new Web3(_config.RpcUrl);
            }
            Console.WriteLine($"[Ethereum Backend] Initialized with MountPath: {MountPath}");
        }
        catch (Exception ex) {
            Console.WriteLine($"[Ethereum Backend] Failed to initialize: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    public INinePFileSystem GetFileSystem()
    {
        if (_config == null || _web3 == null) throw new InvalidOperationException("Backend not initialized");
        return new EthereumFileSystem(_config, _web3, _vault);
    }

    public INinePFileSystem GetFileSystem(SecureString? credentials)
    {
        if (_config == null) throw new InvalidOperationException("Backend not initialized");

        string? rpcUrl = null;
        if (credentials != null)
        {
            // If credentials contain an RPC URL, we have to convert it for Nethereum.
            // This is a known leakage point if RPC URLs contain API keys.
            IntPtr ptr = Marshal.SecureStringToGlobalAllocUnicode(credentials);
            try {
                rpcUrl = Marshal.PtrToStringUni(ptr);
            }
            finally {
                Marshal.ZeroFreeGlobalAllocUnicode(ptr);
            }
        }

        if (rpcUrl == null && !string.IsNullOrEmpty(_config.VaultKey))
        {
            // Look up credentials stored in the vault under the configured key
            var seed = _vault.DeriveSeed(_config.VaultKey, System.Text.Encoding.UTF8.GetBytes(_config.VaultKey));
            var hiddenId = _vault.GenerateHiddenId(seed);
            var vaultFile = _vault.GetVaultPath($"secret_{hiddenId}.vlt");
            if (System.IO.File.Exists(vaultFile))
            {
                var raw = System.IO.File.ReadAllBytes(vaultFile);
                // Use DecryptToBytes to minimize exposure, but Nethereum still needs a string for the RPC URL.
                var decrypted = _vault.DecryptToBytes(raw, _config.VaultKey);
                if (decrypted != null)
                {
                    using (decrypted)
                    {
                        rpcUrl = System.Text.Encoding.UTF8.GetString(decrypted.Span);
                    }
                }
            }
        }

        IWeb3 web3 = rpcUrl != null
            ? new Web3(rpcUrl)
            : (_web3 ?? throw new InvalidOperationException("Backend not initialized"));

        return new EthereumFileSystem(_config, web3, _vault);
    }
}
