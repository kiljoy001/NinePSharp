using System;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Server.Backends.JsonRpc;

public class JsonRpcBackend : IProtocolBackend
{
    private JsonRpcBackendConfig? _config;
    private readonly ILuxVaultService _vault;

    public JsonRpcBackend(ILuxVaultService vault)
    {
        _vault = vault;
    }

    public string Name => "JsonRpc";
    public string MountPath => _config?.MountPath ?? "/jsonrpc";

    public Task InitializeAsync(IConfiguration configuration)
    {
        _config = configuration.Get<JsonRpcBackendConfig>();
        Console.WriteLine($"[JsonRpc Backend] Initialized with MountPath: {MountPath}, {_config?.Endpoints.Count ?? 0} endpoint(s) configured.");
        return Task.CompletedTask;
    }

    public INinePFileSystem GetFileSystem() => GetFileSystem(null);

    public INinePFileSystem GetFileSystem(SecureString? credentials)
    {
        if (_config == null) throw new InvalidOperationException("Backend not initialized");

        // Credential resolution: client-supplied → vault → config
        string user = _config.RpcUser;
        string password = _config.RpcPassword;

        if (credentials != null)
        {
            // Client supplied "user:password" via the 9P auth fid
            IntPtr ptr = Marshal.SecureStringToGlobalAllocUnicode(credentials);
            try {
                string credsStr = Marshal.PtrToStringUni(ptr)!;
                var parts = credsStr.Split(':', 2);
                user = parts[0];
                password = parts.Length == 2 ? parts[1] : string.Empty;
            }
            finally {
                Marshal.ZeroFreeGlobalAllocUnicode(ptr);
            }
        }
        else if (!string.IsNullOrEmpty(_config.VaultKey))
        {
            // Look up "user:password" from vault
            var seed = _vault.DeriveSeed(_config.VaultKey, Encoding.UTF8.GetBytes(_config.VaultKey));
            var hiddenId = _vault.GenerateHiddenId(seed);
            var vaultFile = $"secret_{hiddenId}.vlt";
            if (File.Exists(vaultFile))
            {
                var raw = File.ReadAllBytes(vaultFile);
                var storedBytes = _vault.DecryptToBytes(raw, _config.VaultKey);
                if (storedBytes != null) {
                    try {
                        var stored = Encoding.UTF8.GetString(storedBytes);
                        var parts = stored.Split(':', 2);
                        user = parts[0];
                        password = parts.Length == 2 ? parts[1] : string.Empty;
                    }
                    finally {
                        Array.Clear(storedBytes);
                    }
                }
            }
        }

        var rpc = new JsonRpcTransport(_config.EndpointUrl, user, password);
        return new JsonRpcFileSystem(_config, rpc);
    }
}
