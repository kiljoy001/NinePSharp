using System;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Backends.MQTT;

public class MqttBackend : IProtocolBackend
{
    private MqttBackendConfig? _config;
    private readonly ILuxVaultService _vault;

    public MqttBackend(ILuxVaultService vault)
    {
        _vault = vault;
    }

    public string Name => "MQTT";
    public string MountPath => _config?.MountPath ?? "/mqtt";

    public Task InitializeAsync(IConfiguration configuration)
    {
        _config = configuration.Get<MqttBackendConfig>();
        Console.WriteLine($"[MQTT Backend] Initialized with Broker: {_config?.BrokerUrl}");
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
        
        var transport = new MqttTransport();
        
        string? user = null;
        string? pass = null;
        string? credsStr = SecureStringToString(credentials);
        if (credsStr != null)
        {
            var parts = credsStr.Split(':', 2);
            user = parts[0];
            pass = parts.Length > 1 ? parts[1] : "";
        }

        // Connect asynchronously (could be optimized to happen lazily)
        _ = transport.ConnectAsync(_config.BrokerUrl, _config.ClientId, user, pass);

        return new MqttFileSystem(_config, transport, _vault);
    }
}
