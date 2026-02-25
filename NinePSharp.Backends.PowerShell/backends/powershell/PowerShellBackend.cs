using System;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Backends.PowerShell;

/// <summary>
/// Implements a protocol translation backend that provides object-oriented PowerShell compute resources via 9P.
/// </summary>
public class PowerShellBackend : IProtocolBackend
{
    private string _mountPath = "/ps";

    /// <inheritdoc />
    public string Name => "powershell";

    /// <inheritdoc />
    public string MountPath => _mountPath;

    /// <inheritdoc />
    public Task InitializeAsync(IConfiguration configuration)
    {
        var mount = configuration["MountPath"];
        if (!string.IsNullOrEmpty(mount)) _mountPath = mount;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public INinePFileSystem GetFileSystem(X509Certificate2? certificate = null)
    {
        return new PowerShellFileSystem();
    }

    /// <inheritdoc />
    public INinePFileSystem GetFileSystem(SecureString? credentials, X509Certificate2? certificate = null) => GetFileSystem(certificate);
}
