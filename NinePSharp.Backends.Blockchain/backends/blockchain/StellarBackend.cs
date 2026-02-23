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

public class StellarBackend : IProtocolBackend
{
    private StellarBackendConfig? _config;
    private HttpClient? _httpClient;
    private readonly ILuxVaultService _vault;
    private readonly IEmercoinAuthService? _authService;

    public StellarBackend(ILuxVaultService vault, IEmercoinAuthService? authService = null)
    {
        _vault = vault;
        _authService = authService;
    }

    public string Name => "Stellar";
    public string MountPath => _config?.MountPath ?? "/stellar";

    public Task InitializeAsync(IConfiguration configuration)
    {
        _config = configuration.GetSection("Server:Stellar").Get<StellarBackendConfig>();
        _httpClient = new HttpClient();
        Console.WriteLine($"[Stellar Backend] Initialized with MountPath: {MountPath}");
        return Task.CompletedTask;
    }

    private JsonRpcClient? GetRpcClient()
    {
        if (_config == null || _httpClient == null || string.IsNullOrEmpty(_config.HorizonUrl)) return null;
        // Stellar Horizon is REST, but some setups use JSON-RPC bridges
        return new JsonRpcClient(_httpClient, _config.HorizonUrl);
    }

    public INinePFileSystem GetFileSystem(X509Certificate2? certificate = null)
    {
        if (_config == null) throw new InvalidOperationException("Backend not initialized");
        return new StellarFileSystem(_config, GetRpcClient(), _vault, _authService, certificate);
    }

    public INinePFileSystem GetFileSystem(SecureString? credentials, X509Certificate2? certificate = null) => GetFileSystem(certificate);
}
