using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using System;

namespace NinePSharp.Server.Backends;

public class CardanoBackend : IProtocolBackend
{
    private CardanoBackendConfig? _config;
    private HttpClient? _httpClient;
    private readonly ILuxVaultService _vault;
    private readonly IEmercoinAuthService? _authService;

    public CardanoBackend(ILuxVaultService vault, IEmercoinAuthService? authService = null)
    {
        _vault = vault;
        _authService = authService;
    }

    public string Name => "Cardano";
    public string MountPath => _config?.MountPath ?? "/ada";

    public Task InitializeAsync(IConfiguration configuration)
    {
        _config = configuration.GetSection("Server:Cardano").Get<CardanoBackendConfig>();
        _httpClient = new HttpClient();
        return Task.CompletedTask;
    }

    private JsonRpcClient? GetRpcClient()
    {
        if (_config == null || _httpClient == null || string.IsNullOrEmpty(_config.RpcUrl)) return null;
        return new JsonRpcClient(_httpClient, _config.RpcUrl);
    }

    public IBackendRuntime GetRuntime(X509Certificate2? certificate = null)
    {
        if (_config == null) throw new InvalidOperationException("Backend not initialized");
        return BackendTargetDescriptor.LocalRuntime(Name, MountPath, () => new CardanoFileSystem(_config, GetRpcClient(), _vault, _authService, certificate)).CreateRuntime();
    }

    public IBackendRuntime GetRuntime(SecureString? credentials, X509Certificate2? certificate = null)
    {
        return GetRuntime(certificate);
    }
}
