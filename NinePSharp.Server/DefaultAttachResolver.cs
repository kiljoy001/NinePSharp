using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Server;

internal sealed class DefaultAttachResolver : IAttachResolver
{
    private readonly List<IProtocolBackend> _backends;
    private readonly IRemoteMountProvider _remoteMountProvider;

    public DefaultAttachResolver(IEnumerable<IProtocolBackend> backends, IRemoteMountProvider remoteMountProvider)
    {
        _backends = backends.ToList();
        _remoteMountProvider = remoteMountProvider;
    }

    public async Task<AttachResolution> ResolveAsync(string? aname, SecureString? credentials, X509Certificate2? certificate)
    {
        if (string.IsNullOrEmpty(aname) || aname == "/")
        {
            return new AttachResolution(target: null, isRoot: true);
        }

        var backend = _backends.FirstOrDefault(b => b.MountPath == aname || b.MountPath == "/" + aname || b.Name == aname);
        if (backend != null)
        {
            return new AttachResolution(
                BackendTargetDescriptor.LocalRuntime(
                    backend.Name,
                    backend.MountPath,
                    () => backend.GetRuntime(credentials, certificate)),
                isRoot: false);
        }

        var remotePath = aname.StartsWith("/", StringComparison.Ordinal) ? aname : "/" + aname;
        var remoteMounts = await GetRemoteMountPathsAsync();
        if (remoteMounts.Any(path => NormalizeMountPath(path) == NormalizeMountPath(remotePath)))
        {
            return new AttachResolution(BackendTargetDescriptor.Remote(remotePath, remotePath), isRoot: false);
        }

        throw new Utils.NinePProtocolException($"No backend found for aname '{aname}'");
    }

    public IReadOnlyList<NamespaceMountDescriptor> GetRootMounts(X509Certificate2? certificate)
    {
        var mounts = new List<NamespaceMountDescriptor>();
        foreach (var backend in _backends)
        {
            if (string.IsNullOrWhiteSpace(backend.MountPath))
            {
                continue;
            }

            mounts.Add(new NamespaceMountDescriptor(
                backend.MountPath,
                BackendTargetDescriptor.LocalRuntime(
                    backend.Name,
                    backend.MountPath,
                    () => backend.GetRuntime(certificate))));
        }

        return mounts;
    }

    public async Task<IReadOnlyList<string>> GetRemoteMountPathsAsync()
    {
        var task = _remoteMountProvider.GetRemoteMountPathsAsync();
        if (task == null)
        {
            return Array.Empty<string>();
        }

        return await task ?? Array.Empty<string>();
    }

    public async Task<IBackendRuntime?> TryCreateRemoteRuntimeAsync(string mountPath)
    {
        var task = _remoteMountProvider.TryCreateRemoteRuntimeAsync(mountPath);
        if (task == null)
        {
            return null;
        }

        return await task;
    }

    private static string NormalizeMountPath(string mountPath)
    {
        if (string.IsNullOrWhiteSpace(mountPath))
        {
            return "/";
        }

        var trimmed = mountPath.Trim();
        if (!trimmed.StartsWith("/", StringComparison.Ordinal))
        {
            trimmed = "/" + trimmed;
        }

        return trimmed;
    }
}
