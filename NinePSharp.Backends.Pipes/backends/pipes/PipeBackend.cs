using System;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Backends.Pipes;

public class PipeBackend : IProtocolBackend
{
    private PipeFileSystem? _prototypeFs;

    public string Name => "ipc";
    public string MountPath { get; private set; } = "/ipc";

    public Task InitializeAsync(IConfiguration configuration)
    {
        MountPath = configuration["MountPath"] ?? MountPath;
        _prototypeFs = new PipeFileSystem();
        return Task.CompletedTask;
    }

    public INinePFileSystem GetFileSystem(X509Certificate2? certificate = null) => (_prototypeFs ?? throw new InvalidOperationException("IPC backend not initialized.")).Clone();
    public INinePFileSystem GetFileSystem(SecureString? credentials, X509Certificate2? certificate = null) => GetFileSystem(certificate);
}
