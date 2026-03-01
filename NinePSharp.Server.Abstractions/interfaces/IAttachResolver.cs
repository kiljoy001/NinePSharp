using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Abstractions.Utils;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace NinePSharp.Server.Interfaces;

public interface IAttachResolver
{
    Task<AttachResolution> ResolveAsync(string? aname, SecureString? credentials, X509Certificate2? certificate);
    IReadOnlyList<NamespaceMountDescriptor> GetRootMounts(X509Certificate2? certificate);
    Task<IReadOnlyList<string>> GetRemoteMountPathsAsync();
    Task<IBackendRuntime?> TryCreateRemoteRuntimeAsync(string mountPath);
}

public sealed class BackendTargetDescriptor
{
    private readonly Func<IBackendRuntime>? _createRuntime;

    private BackendTargetDescriptor(string id, string mountPath, bool isRemote, Func<IBackendRuntime>? createRuntime)
    {
        Id = id;
        MountPath = mountPath;
        IsRemote = isRemote;
        _createRuntime = createRuntime;
    }

    public string Id { get; }

    public string MountPath { get; }

    public bool IsRemote { get; }

    public static BackendTargetDescriptor Local(string id, string mountPath, Func<INinePFileSystem> createSession)
        => new(id, mountPath, isRemote: false, () => new FileSystemBackendRuntime(id, mountPath, createSession));

    public static BackendTargetDescriptor LocalRuntime(string id, string mountPath, Func<IBackendRuntime> createRuntime)
        => new(id, mountPath, isRemote: false, createRuntime);

    public static BackendTargetDescriptor Remote(string id, string mountPath)
        => new(id, mountPath, isRemote: true, createRuntime: null);

    public IBackendRuntime CreateRuntime()
    {
        if (IsRemote)
        {
            throw new InvalidOperationException("Remote backend targets must be materialized through the remote mount provider.");
        }

        if (_createRuntime == null)
        {
            throw new InvalidOperationException("Backend target descriptor is missing creation delegate.");
        }

        return _createRuntime();
    }
}

public sealed class NamespaceMountDescriptor
{
    public NamespaceMountDescriptor(string mountPath, BackendTargetDescriptor target)
    {
        MountPath = mountPath;
        Target = target;
    }

    public string MountPath { get; }

    public BackendTargetDescriptor Target { get; }
}

public sealed class AttachResolution
{
    public AttachResolution(BackendTargetDescriptor? target, bool isRoot)
    {
        Target = target;
        IsRoot = isRoot;
    }

    public BackendTargetDescriptor? Target { get; }

    public bool IsRoot { get; }
}
