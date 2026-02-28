using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Server;

public sealed class NullRemoteMountProvider : IRemoteMountProvider
{
    public void Start()
    {
    }

    public Task StopAsync() => Task.CompletedTask;

    public Task RegisterMountAsync(string mountPath, Func<INinePFileSystem> createSession) => Task.CompletedTask;

    public Task<IReadOnlyList<string>> GetRemoteMountPathsAsync()
        => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

    public Task<INinePFileSystem?> TryCreateRemoteFileSystemAsync(string mountPath)
        => Task.FromResult<INinePFileSystem?>(null);

    public void Dispose()
    {
    }
}
