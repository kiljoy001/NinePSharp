using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Backends.REST;

public class RestBackend : IProtocolBackend
{
    private RestBackendConfig? _config;
    private readonly ILuxVaultService _vault;

    public RestBackend(ILuxVaultService vault)
    {
        _vault = vault;
    }

    public string Name => "REST";
    public string MountPath => _config?.MountPath ?? "/rest";

    public Task InitializeAsync(IConfiguration configuration)
    {
        _config = configuration.Get<RestBackendConfig>();
        Console.WriteLine($"[REST Backend] Initialized with BaseUrl: {_config?.BaseUrl}, MountPath: {MountPath}");
        return Task.CompletedTask;
    }

    public INinePFileSystem GetFileSystem() => GetFileSystem(null);

    private string? SecureStringToString(SecureString? ss)
    {
        if (ss == null) return null;
        IntPtr ptr = Marshal.SecureStringToGlobalAllocUnicode(ss);
        try { return Marshal.PtrToStringUni(ptr); }
        finally { Marshal.ZeroFreeGlobalAllocUnicode(ptr); }
    }

    public INinePFileSystem GetFileSystem(SecureString? credentials)
    {
        if (_config == null) throw new InvalidOperationException("Backend not initialized");
        
        var client = new HttpClient();
        if (!string.IsNullOrEmpty(_config.BaseUrl))
        {
            client.BaseAddress = new Uri(_config.BaseUrl.EndsWith("/") ? _config.BaseUrl : _config.BaseUrl + "/");
        }

        string? credsStr = SecureStringToString(credentials);
        if (!string.IsNullOrEmpty(credsStr))
        {
            var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes(credsStr));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", encoded);
        }

        return new RestFileSystem(_config, client, _vault);
    }
}
