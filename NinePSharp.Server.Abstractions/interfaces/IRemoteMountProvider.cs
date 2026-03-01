using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NinePSharp.Server.Interfaces;

public interface IRemoteMountProvider : IDisposable
{
    void Start();
    Task StopAsync();
    Task RegisterMountAsync(string mountPath, Func<IBackendRuntime> createRuntime);
    Task<IReadOnlyList<string>> GetRemoteMountPathsAsync();
    Task<IBackendRuntime?> TryCreateRemoteRuntimeAsync(string mountPath);
}
