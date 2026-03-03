using System.Threading;
using System.Threading.Tasks;
using NinePSharp.Messages;

namespace NinePSharp.Server.Interfaces;

/// <summary>
/// The universal host interface for 9P request handling.
///
/// The core NinePSharp engine manages:
/// - FID lifecycle and validation
/// - Namespace traversal and mount crossing
/// - Path resolution (from root through mounts to relative path)
/// - Tflush cancellation coordination
/// - Message encoding/decoding
///
/// The host implements this interface to handle actual I/O:
/// - File/database/memory reads and writes
/// - Directory enumeration
/// - Stat queries
/// - Authentication (if needed)
/// </summary>
public interface INinePRequestHandler
{
    /// <summary>
    /// Walk to a path within the backend.
    /// The engine has already resolved namespace mounts; this is the final backend walk.
    /// </summary>
    /// <param name="relativePath">Path segments from backend root</param>
    /// <param name="msg">Original Twalk message</param>
    /// <param name="ct">Cancellation token (from Tflush)</param>
    /// <returns>Rwalk with Qids for successfully walked segments</returns>
    Task<Rwalk> WalkAsync(string[] relativePath, Twalk msg, CancellationToken ct);

    /// <summary>
    /// Open a file or directory.
    /// </summary>
    Task<Ropen> OpenAsync(string[] relativePath, Topen msg, CancellationToken ct);

    /// <summary>
    /// Read from a file.
    /// For directories: if backend supports readdir natively, return entries here.
    /// Otherwise, return error and let Treaddir handle it.
    /// </summary>
    Task<Rread> ReadAsync(string[] relativePath, Tread msg, CancellationToken ct);

    /// <summary>
    /// Write to a file.
    /// </summary>
    Task<Rwrite> WriteAsync(string[] relativePath, Twrite msg, CancellationToken ct);

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
    /// <param name="parentPath">Path to parent directory</param>
    /// <param name="msg">Tcreate with name and permissions</param>
    Task<Rcreate> CreateAsync(string[] parentPath, Tcreate msg, CancellationToken ct);

    /// <summary>
    /// Remove a file or directory.
    /// </summary>
    Task<Rremove> RemoveAsync(string[] relativePath, Tremove msg, CancellationToken ct);

    /// <summary>
    /// Read directory entries (9P2000.L Treaddir).
    /// Optional: return null/throw to fall back to Tread-based readdir.
    /// </summary>
    Task<Rreaddir>? ReaddirAsync(string[] relativePath, Treaddir msg, CancellationToken ct);

    /// <summary>
    /// Read from an auth file (afid). Called when Tread targets an auth fid
    /// created by Tauth. Implement auth protocol exchange (e.g., p9any, p9sk1).
    /// Per auth(2): the host reads challenges/results from the auth channel.
    /// </summary>
    /// <param name="afid">The auth fid number</param>
    /// <param name="offset">Read offset</param>
    /// <param name="count">Maximum bytes to read</param>
    /// <param name="ct">Cancellation token (from Tflush)</param>
    /// <returns>Auth protocol response data</returns>
    Task<byte[]> AuthReadAsync(uint afid, ulong offset, uint count, CancellationToken ct);

    /// <summary>
    /// Write to an auth file (afid). Called when Twrite targets an auth fid.
    /// Per auth(2): the host writes auth requests/challenges to the auth channel.
    /// </summary>
    /// <param name="afid">The auth fid number</param>
    /// <param name="offset">Write offset</param>
    /// <param name="data">Auth protocol request data</param>
    /// <param name="ct">Cancellation token (from Tflush)</param>
    /// <returns>Number of bytes consumed</returns>
    Task<uint> AuthWriteAsync(uint afid, ulong offset, byte[] data, CancellationToken ct);
}
