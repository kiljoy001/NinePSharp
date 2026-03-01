using System.Threading.Tasks;
using NinePSharp.Server.Utils;
using NinePSharp.Messages;
using NinePSharp.Constants;

namespace NinePSharp.Server.Interfaces;

/// <summary>
/// Core 9P2000 file system interface.
/// Strictly implements the Plan 9 9P2000 protocol without .u or .L extensions.
/// </summary>
public interface INinePFileSystem
{
    /// <summary>
    /// Negotiated protocol dialect.
    /// </summary>
    NinePDialect Dialect { get; set; }

    /// <summary>
    /// Walks a path in the filesystem.
    /// </summary>
    Task<Rwalk> WalkAsync(Twalk twalk);

    /// <summary>
    /// Opens a file or directory for reading or writing.
    /// </summary>
    Task<Ropen> OpenAsync(Topen topen);

    /// <summary>
    /// Reads data from a file or directory.
    /// </summary>
    Task<Rread> ReadAsync(Tread tread);

    /// <summary>
    /// Writes data to a file.
    /// </summary>
    Task<Rwrite> WriteAsync(Twrite twrite);

    /// <summary>
    /// Closes a file descriptor.
    /// </summary>
    Task<Rclunk> ClunkAsync(Tclunk tclunk);

    /// <summary>
    /// Retrieves status information for a file.
    /// </summary>
    Task<Rstat> StatAsync(Tstat tstat);

    /// <summary>
    /// Updates status information for a file.
    /// </summary>
    Task<Rwstat> WstatAsync(Twstat twstat);

    /// <summary>
    /// Removes a file or directory.
    /// </summary>
    Task<Rremove> RemoveAsync(Tremove tremove);

    /// <summary>
    /// Creates a new file or directory.
    /// </summary>
    Task<Rcreate> CreateAsync(Tcreate tcreate);

    /// <summary>
    /// Creates a shallow clone of the current filesystem state for use with a new FID.
    /// </summary>
    INinePFileSystem Clone();
}
