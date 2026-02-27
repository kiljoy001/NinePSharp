using System.Security.Cryptography.X509Certificates;
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

    public INinePFileSystem GetFileSystem(X509Certificate2? certificate = null) => GetFileSystem(null);

    public INinePFileSystem GetFileSystem(SecureString? credentials, X509Certificate2? certificate = null)
    {
        if (_config == null) throw new InvalidOperationException("Backend not initialized");
        
        var transport = new MqttTransport();
        
        SecureString? user = null;
        SecureString? pass = null;

        if (credentials != null)
        {
            // Format "user:pass"
            string credsStr = SecureStringHelper.ToString(credentials);
            var parts = credsStr.Split(':', 2);
            if (parts.Length == 2)
            {
                user = new SecureString();
                foreach (char c in parts[0]) user.AppendChar(c);
                pass = new SecureString();
                foreach (char c in parts[1]) pass.AppendChar(c);
            }
        }

        // Connect asynchronously (Zero-exposure: user/pass are SecureStrings)
        _ = transport.ConnectAsync(_config.BrokerUrl, _config.ClientId, user, pass);

        return new MqttFileSystem(_config, transport, _vault);
    }
}
