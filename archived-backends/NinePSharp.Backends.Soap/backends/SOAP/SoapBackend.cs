using System.Security.Cryptography.X509Certificates;
using System;
using System.Security;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Backends.SOAP;

/// <summary>
/// Backend implementation for the SOAP protocol.
/// </summary>
public class SoapBackend : IProtocolBackend
{
    private SoapBackendConfig? _config;
    private readonly ILuxVaultService _vault;

    /// <summary>
    /// Initializes a new instance of the <see cref="SoapBackend"/> class.
    /// </summary>
    /// <param name="vault">The vault service.</param>
    public SoapBackend(ILuxVaultService vault)
    {
        _vault = vault;
    }

    /// <inheritdoc />
    public string Name => "SOAP";
    /// <inheritdoc />
    public string MountPath => _config?.MountPath ?? "/soap";

    /// <inheritdoc />
    public Task InitializeAsync(IConfiguration configuration)
    {
        _config = configuration.Get<SoapBackendConfig>();
        Console.WriteLine($"[SOAP Backend] Initialized with WSDL: {_config?.WsdlUrl}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public INinePFileSystem GetFileSystem(X509Certificate2? certificate = null) => GetFileSystem(null);

    /// <inheritdoc />
    public INinePFileSystem GetFileSystem(SecureString? credentials, X509Certificate2? certificate = null)
    {
        if (_config == null) throw new InvalidOperationException("Backend not initialized");
        
        var transport = new SoapTransport();
        _ = transport.ConnectAsync(_config.WsdlUrl);

        return new SoapFileSystem(_config, transport, _vault);
    }
}
