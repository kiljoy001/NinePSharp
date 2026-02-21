using System;
using System.Security;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Backends.Websockets;

public class WebsocketBackend : IProtocolBackend
{
    private WebsocketBackendConfig? _config;
    private readonly ILuxVaultService _vault;

    public WebsocketBackend(ILuxVaultService vault)
    {
        _vault = vault;
    }

    public string Name => "Websockets";
    public string MountPath => _config?.MountPath ?? "/ws";

    public Task InitializeAsync(IConfiguration configuration)
    {
        _config = configuration.Get<WebsocketBackendConfig>();
        Console.WriteLine($"[Websockets Backend] Initialized with URL: {_config?.Url}");
        return Task.CompletedTask;
    }

    public INinePFileSystem GetFileSystem() => GetFileSystem(null);

    public INinePFileSystem GetFileSystem(SecureString? credentials)
    {
        if (_config == null) throw new InvalidOperationException("Backend not initialized");
        
        var transport = new WebsocketTransport();
        _ = transport.ConnectAsync(_config.Url);

        return new WebsocketFileSystem(_config, transport, _vault);
    }
}
