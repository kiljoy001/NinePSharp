using System.Threading;
using System.Threading.Tasks;
using NinePSharp.Messages;

namespace NinePSharp.Server.Interfaces;

/// <summary>
/// Handles authentication handshakes for 9P sessions.
/// </summary>
public interface IAuthHandler
{
    /// <summary>
    /// Read from the auth channel.
    /// </summary>
    Task<byte[]> ReadAsync(ulong offset, uint count, CancellationToken ct);

    /// <summary>
    /// Write to the auth channel.
    /// </summary>
    Task<uint> WriteAsync(ulong offset, byte[] data, CancellationToken ct);
}

/// <summary>
/// The universal host interface for 9P request handling.
/// Implementation is focused on mirroring the simplicity of go9p.
/// </summary>
public interface INinePRequestHandler
{
    /// <summary>
    /// Optional: Return an auth handler for a given Tauth request.
    /// Returns null if authentication is not required or supported.
    /// </summary>
    Task<IAuthHandler?> GetAuthHandlerAsync(Tauth msg, CancellationToken ct);

    /// <summary>
    /// Attach to a file tree (Tattach).
    /// </summary>
    Task<Rattach> AttachAsync(Tattach msg, CancellationToken ct);

    /// <summary>
    /// Walk to a path within the backend.
    /// </summary>
    Task<Rwalk> WalkAsync(string[] relativePath, Twalk msg, CancellationToken ct);

    /// <summary>
    /// Open a file or directory.
    /// </summary>
    Task<Ropen> OpenAsync(string[] relativePath, Topen msg, CancellationToken ct);

    /// <summary>
    /// Read from a file.
    /// </summary>
    Task<Rread> ReadAsync(string[] relativePath, Tread msg, CancellationToken ct);

    /// <summary>
    /// Write to a file.
    /// </summary>
    Task<Rwrite> WriteAsync(string[] relativePath, Twrite msg, CancellationToken ct);

    /// <summary>
    /// Close a fid (Tclunk).
    /// </summary>
    Task<Rclunk> ClunkAsync(string[] relativePath, Tclunk msg, CancellationToken ct);

    /// <summary>
    /// Get file/directory metadata.
    /// </summary>
    Task<Rstat> StatAsync(string[] relativePath, Tstat msg, CancellationToken ct);

    /// <summary>
    /// Update file/directory metadata.
    /// </summary>
    Task<Rwstat> WstatAsync(string[] relativePath, Twstat msg, CancellationToken ct);

    /// <summary>
    /// Create a new file or directory.
    /// </summary>
    Task<Rcreate> CreateAsync(string[] parentPath, Tcreate msg, CancellationToken ct);

    /// <summary>
    /// Remove a file or directory.
    /// </summary>
    Task<Rremove> RemoveAsync(string[] relativePath, Tremove msg, CancellationToken ct);

    /// <summary>
    /// Read directory entries (9P2000.L Treaddir).
    /// </summary>
    Task<Rreaddir>? ReaddirAsync(string[] relativePath, Treaddir msg, CancellationToken ct);

    Task<Rsymlink> SymlinkAsync(string[] relativePath, Tsymlink msg, CancellationToken ct);
    Task<Rreadlink> ReadlinkAsync(string[] relativePath, Treadlink msg, CancellationToken ct);
    Task<Rlink> LinkAsync(string[] relativePath, Tlink msg, CancellationToken ct);

    // 9P2000.L Locking
    Task<Rlerror> LockAsync(string[] relativePath, Tlock msg, CancellationToken ct);
    Task<Rgetlock> GetlockAsync(string[] relativePath, Tgetlock msg, CancellationToken ct);

    // 9P2000.L Xattr
    Task<Rxattrwalk> XattrwalkAsync(string[] relativePath, Txattrwalk msg, CancellationToken ct);
    Task<Rxattrcreate> XattrcreateAsync(string[] relativePath, Txattrcreate msg, CancellationToken ct);

    Task<Rflush> FlushAsync(Tflush msg, CancellationToken ct);
}
