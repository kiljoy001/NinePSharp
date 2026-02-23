using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Backends;

public class SolanaBackend : IProtocolBackend
{
    private SolanaBackendConfig? _config;
    private HttpClient? _httpClient;
    private readonly ILuxVaultService _vault;
    private readonly IEmercoinAuthService? _authService;

    public SolanaBackend(ILuxVaultService vault, IEmercoinAuthService? authService = null)
    {
        _vault = vault;
        _authService = authService;
    }

    public string Name => "Solana";
    public string MountPath => _config?.MountPath ?? "/sol";

    public Task InitializeAsync(IConfiguration configuration)
    {
        _config = configuration.GetSection("Server:Solana").Get<SolanaBackendConfig>();
        _httpClient = new HttpClient();
        Console.WriteLine($"[Solana Backend] Initialized with MountPath: {MountPath}");
        return Task.CompletedTask;
    }

    private JsonRpcClient? GetRpcClient()
    {
        if (_config == null || _httpClient == null || string.IsNullOrEmpty(_config.RpcUrl)) return null;
        return new JsonRpcClient(_httpClient, _config.RpcUrl);
    }

    public INinePFileSystem GetFileSystem(X509Certificate2? certificate = null)
    {
        if (_config == null) throw new InvalidOperationException("Backend not initialized");
        return new SolanaFileSystem(_config, GetRpcClient(), _vault, _authService, certificate);
    }

    public INinePFileSystem GetFileSystem(SecureString? credentials, X509Certificate2? certificate = null) => GetFileSystem(certificate);
}
