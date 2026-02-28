using NinePSharp.Server.Interfaces;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace NinePSharp.Server.Interfaces;

public interface IAttachResolver
{
    Task<AttachResolution> ResolveAsync(string? aname, SecureString? credentials, X509Certificate2? certificate);
    IReadOnlyList<NamespaceMountDescriptor> GetRootMounts(X509Certificate2? certificate);
    Task<IReadOnlyList<string>> GetRemoteMountPathsAsync();
    Task<INinePFileSystem?> TryCreateRemoteFileSystemAsync(string mountPath);
}

public sealed class BackendTargetDescriptor
{
    private readonly Func<INinePFileSystem>? _createSession;

    private BackendTargetDescriptor(string id, string mountPath, bool isRemote, Func<INinePFileSystem>? createSession)
    {
        Id = id;
        MountPath = mountPath;
        IsRemote = isRemote;
        _createSession = createSession;
    }

    public string Id { get; }

    public string MountPath { get; }

    public bool IsRemote { get; }

    public static BackendTargetDescriptor Local(string id, string mountPath, Func<INinePFileSystem> createSession)
        => new(id, mountPath, isRemote: false, createSession);

    public static BackendTargetDescriptor Remote(string mountPath)
        => new(mountPath, mountPath, isRemote: true, createSession: null);

    public INinePFileSystem CreateSession()
    {
        if (IsRemote || _createSession == null)
        {
            throw new InvalidOperationException("Remote backend targets must be materialized through the remote mount provider.");
        }

        return _createSession();
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
