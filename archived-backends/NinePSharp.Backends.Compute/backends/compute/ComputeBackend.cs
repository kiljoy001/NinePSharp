using System;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Backends.Compute;

/// <summary>
/// Implements a protocol translation backend that provides sandboxed WASM compute resources via 9P.
/// </summary>
public class ComputeBackend : IProtocolBackend
{
    private ComputeBackendConfig? _config;

    /// <inheritdoc />
    public string Name => "compute";

    /// <inheritdoc />
    public string MountPath => _config?.MountPath ?? "/compute";

    /// <inheritdoc />
    public Task InitializeAsync(IConfiguration configuration)
    {
        _config = new ComputeBackendConfig();
        configuration.Bind(_config);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public INinePFileSystem GetFileSystem(X509Certificate2? certificate = null)
    {
        if (_config == null) throw new InvalidOperationException("Backend not initialized.");
        return new ComputeFileSystem(_config);
    }

    /// <inheritdoc />
    public INinePFileSystem GetFileSystem(SecureString? credentials, X509Certificate2? certificate = null) => GetFileSystem(certificate);
}
