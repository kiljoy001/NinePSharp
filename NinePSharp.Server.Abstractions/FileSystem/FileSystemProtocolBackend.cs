using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Server.FileSystem;

public class FileSystemProtocolBackend : IProtocolBackend
{
    private readonly NinePDir root;

    public FileSystemProtocolBackend(NinePDir root, string mountPath = "/")
    {
        this.root = root;
        MountPath = mountPath;
    }

    public string Name => "FileSystem";

    public string MountPath { get; private set; }

    public Task InitializeAsync(IConfiguration configuration)
    {
        MountPath = configuration["MountPath"] ?? MountPath;
        return Task.CompletedTask;
    }

    public INinePFileSystem GetFileSystem(X509Certificate2? certificate = null)
    {
        return new FileSystemWrapper(new FileSystemBackend(root, MountPath));
    }
}
