using System.Security.Cryptography.X509Certificates;
using System;
using System.Security;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Backends.gRPC;

/// <summary>
/// Backend implementation for the gRPC protocol.
/// </summary>
public class GrpcBackend : IProtocolBackend
{
    private GrpcBackendConfig? _config;
    private readonly ILuxVaultService _vault;

    /// <summary>
    /// Initializes a new instance of the <see cref="GrpcBackend"/> class.
    /// </summary>
    /// <param name="vault">The vault service.</param>
    public GrpcBackend(ILuxVaultService vault)
    {
        _vault = vault;
    }

    /// <inheritdoc />
    public string Name => "gRPC";
    /// <inheritdoc />
    public string MountPath => _config?.MountPath ?? "/grpc";

    /// <inheritdoc />
    public Task InitializeAsync(IConfiguration configuration)
    {
        _config = configuration.Get<GrpcBackendConfig>();
        Console.WriteLine($"[gRPC Backend] Initialized with Host: {_config?.Host}:{_config?.Port}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public INinePFileSystem GetFileSystem(X509Certificate2? certificate = null) => GetFileSystem(null);

    /// <inheritdoc />
    public INinePFileSystem GetFileSystem(SecureString? credentials, X509Certificate2? certificate = null)
    {
        if (_config == null) throw new InvalidOperationException("Backend not initialized");
        
        var transport = new GrpcTransport();
        _ = transport.ConnectAsync(_config.Host, _config.Port);

        return new GrpcFileSystem(_config, transport, _vault);
    }
}
