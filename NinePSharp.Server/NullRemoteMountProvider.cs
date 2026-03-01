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

    public Task RegisterMountAsync(string mountPath, Func<IBackendRuntime> createRuntime) => Task.CompletedTask;

    public Task<IReadOnlyList<string>> GetRemoteMountPathsAsync()
        => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

    public Task<IBackendRuntime?> TryCreateRemoteRuntimeAsync(string mountPath)
        => Task.FromResult<IBackendRuntime?>(null);

    public void Dispose()
    {
    }
}
