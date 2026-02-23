using System.Security.Cryptography.X509Certificates;
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

    public INinePFileSystem GetFileSystem(X509Certificate2? certificate = null) => GetFileSystem(null);

    public INinePFileSystem GetFileSystem(SecureString? credentials, X509Certificate2? certificate = null)
    {
        if (_config == null) throw new InvalidOperationException("Backend not initialized");
        
        var client = new HttpClient();
        if (!string.IsNullOrEmpty(_config.BaseUrl))
        {
            client.BaseAddress = new Uri(_config.BaseUrl.EndsWith("/") ? _config.BaseUrl : _config.BaseUrl + "/");
        }

        if (credentials != null)
        {
            // Zero-exposure: decode to bytes, then base64 the bytes
            IntPtr ptr = Marshal.SecureStringToGlobalAllocUnicode(credentials);
            try {
                unsafe {
                    byte* pChars = (byte*)ptr.ToPointer();
                    int charCount = credentials.Length;
                    // Basic auth expects "user:pass" in UTF8/ASCII
                    // We'll decode the Unicode SecureString to UTF8 bytes
                    int byteCount = Encoding.UTF8.GetByteCount((char*)pChars, charCount);
                    if (byteCount > 0)
                    {
                        byte[] utf8Bytes = GC.AllocateArray<byte>(byteCount, pinned: true);
                        try {
                            fixed (byte* pUtf8 = utf8Bytes) {
                                Encoding.UTF8.GetBytes((char*)pChars, charCount, pUtf8, byteCount);
                            }
                            var encoded = Convert.ToBase64String(utf8Bytes);
                            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", encoded);
                        }
                        finally {
                            Array.Clear(utf8Bytes);
                        }
                    }
                }
            }
            finally {
                Marshal.ZeroFreeGlobalAllocUnicode(ptr);
            }
        }

        return new RestFileSystem(_config, client, _vault);
    }
}
