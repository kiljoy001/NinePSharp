using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Backends;

/// <summary>
/// Implements a protocol translation backend that exposes the Ethereum blockchain via 9P.
/// </summary>
public class EthereumBackend : IProtocolBackend
{
    private EthereumBackendConfig? _config;
    private HttpClient? _httpClient;
    private readonly ILuxVaultService _vault;
    private readonly IEmercoinAuthService? _authService;

    public EthereumBackend(ILuxVaultService vault, IEmercoinAuthService? authService = null)
    {
        _vault = vault;
        _authService = authService;
    }

    public string Name => "Ethereum";
    public string MountPath => _config?.MountPath ?? "/eth";

    public Task InitializeAsync(IConfiguration configuration)
    {
        try {
            _config = configuration.GetSection("Server:Ethereum").Get<EthereumBackendConfig>();
            _httpClient = new HttpClient();
            Console.WriteLine($"[Ethereum Backend] Initialized with MountPath: {MountPath}");
        }
        catch (Exception ex) {
            Console.WriteLine($"[Ethereum Backend] Failed to initialize: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private JsonRpcClient GetRpcClient(string? overrideUrl = null)
    {
        if (_config == null || _httpClient == null) throw new InvalidOperationException("Backend not initialized");
        return new JsonRpcClient(_httpClient, overrideUrl ?? _config.RpcUrl);
    }

    public INinePFileSystem GetFileSystem(X509Certificate2? certificate = null)
    {
        if (_config == null) throw new InvalidOperationException("Backend not initialized");
        return new EthereumFileSystem(_config, GetRpcClient(), _vault, _authService, certificate);
    }

    public INinePFileSystem GetFileSystem(SecureString? credentials, X509Certificate2? certificate = null)
    {
        if (_config == null) throw new InvalidOperationException("Backend not initialized");

        string? rpcUrl = null;
        if (credentials != null)
        {
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
            using var seed = new SecureBuffer(32, _vault.GetLocalArena());
            _vault.DeriveSeed(_config.VaultKey, System.Text.Encoding.UTF8.GetBytes(_config.VaultKey), seed.Span);
            var hiddenId = _vault.GenerateHiddenId(seed.Span);
            var vaultFile = _vault.GetVaultPath($"secret_{hiddenId}.vlt");
            if (System.IO.File.Exists(vaultFile))
            {
                var raw = System.IO.File.ReadAllBytes(vaultFile);
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

        return new EthereumFileSystem(_config, GetRpcClient(rpcUrl), _vault, _authService, certificate);
    }
}
