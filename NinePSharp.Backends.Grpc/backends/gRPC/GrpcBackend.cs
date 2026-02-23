using System.Security.Cryptography.X509Certificates;
using System;
using System.Security;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Backends.gRPC;

public class GrpcBackend : IProtocolBackend
{
    private GrpcBackendConfig? _config;
    private readonly ILuxVaultService _vault;

    public GrpcBackend(ILuxVaultService vault)
    {
        _vault = vault;
    }

    public string Name => "gRPC";
    public string MountPath => _config?.MountPath ?? "/grpc";

    public Task InitializeAsync(IConfiguration configuration)
    {
        _config = configuration.Get<GrpcBackendConfig>();
        Console.WriteLine($"[gRPC Backend] Initialized with Host: {_config?.Host}:{_config?.Port}");
        return Task.CompletedTask;
    }

    public INinePFileSystem GetFileSystem(X509Certificate2? certificate = null) => GetFileSystem(null);

    public INinePFileSystem GetFileSystem(SecureString? credentials, X509Certificate2? certificate = null)
    {
        if (_config == null) throw new InvalidOperationException("Backend not initialized");
        
        var transport = new GrpcTransport();
        _ = transport.ConnectAsync(_config.Host, _config.Port);

        return new GrpcFileSystem(_config, transport, _vault);
    }
}
