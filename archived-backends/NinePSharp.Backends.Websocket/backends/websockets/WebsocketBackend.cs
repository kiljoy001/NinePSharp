using System.Security.Cryptography.X509Certificates;
using System;
using System.Security;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Backends.Websockets;

/// <summary>
/// Backend implementation for the WebSocket protocol.
/// </summary>
public class WebsocketBackend : IProtocolBackend
{
    private WebsocketBackendConfig? _config;
    private readonly ILuxVaultService _vault;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebsocketBackend"/> class.
    /// </summary>
    /// <param name="vault">The vault service.</param>
    public WebsocketBackend(ILuxVaultService vault)
    {
        _vault = vault;
    }

    /// <inheritdoc />
    public string Name => "Websockets";
    /// <inheritdoc />
    public string MountPath => _config?.MountPath ?? "/ws";

    /// <inheritdoc />
    public Task InitializeAsync(IConfiguration configuration)
    {
        _config = configuration.Get<WebsocketBackendConfig>();
        Console.WriteLine($"[Websockets Backend] Initialized with URL: {_config?.Url}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public INinePFileSystem GetFileSystem(X509Certificate2? certificate = null) => GetFileSystem(null);

    /// <inheritdoc />
    public INinePFileSystem GetFileSystem(SecureString? credentials, X509Certificate2? certificate = null)
    {
        if (_config == null) throw new InvalidOperationException("Backend not initialized");
        
        var transport = new WebsocketTransport();
        // Background connection task
        _ = transport.ConnectAsync(_config.Url);

        return new WebsocketFileSystem(_config, transport, _vault);
    }
}
