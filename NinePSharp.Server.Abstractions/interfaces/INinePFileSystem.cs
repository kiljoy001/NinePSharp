using System.Threading.Tasks;
using NinePSharp.Server.Utils;
using NinePSharp.Messages;
using NinePSharp.Constants;

namespace NinePSharp.Server.Interfaces;

public interface INinePFileSystem
{
    /// <summary>
    /// Negotiated protocol dialect.
    /// </summary>
    NinePDialect Dialect
    {
        get => NinePDialect.NineP2000;
        set { }
    }

    /// <summary>
    /// Walks a path in the filesystem.
    /// </summary>
    /// <param name="twalk">The walk request message.</param>
    /// <returns>A walk response message containing QIDs for the successful path elements.</returns>
    Task<Rwalk> WalkAsync(Twalk twalk);

    /// <summary>
    /// Opens a file or directory for reading or writing.
    /// </summary>
    /// <param name="topen">The open request message.</param>
    /// <returns>An open response message.</returns>
    Task<Ropen> OpenAsync(Topen topen);

    /// <summary>
    /// Reads data from a file.
    /// </summary>
    /// <param name="tread">The read request message.</param>
    /// <returns>A read response message containing the data read.</returns>
    Task<Rread> ReadAsync(Tread tread);

    /// <summary>
    /// Writes data to a file.
    /// </summary>
    /// <param name="twrite">The write request message.</param>
    /// <returns>A write response message confirming the number of bytes written.</returns>
    Task<Rwrite> WriteAsync(Twrite twrite);

    /// <summary>
    /// Closes a file descriptor.
    /// </summary>
    /// <param name="tclunk">The clunk request message.</param>
    /// <returns>A clunk response message.</returns>
    Task<Rclunk> ClunkAsync(Tclunk tclunk);

    /// <summary>
    /// Retrieves status information for a file.
    /// </summary>
    /// <param name="tstat">The stat request message.</param>
    /// <returns>A stat response message containing the file statistics.</returns>
    Task<Rstat> StatAsync(Tstat tstat);

    /// <summary>
    /// Updates status information for a file.
    /// </summary>
    /// <param name="twstat">The wstat request message.</param>
    /// <returns>A wstat response message.</returns>
    Task<Rwstat> WstatAsync(Twstat twstat);

    /// <summary>
    /// Removes a file or directory.
    /// </summary>
    /// <param name="tremove">The remove request message.</param>
    /// <returns>A remove response message.</returns>
    Task<Rremove> RemoveAsync(Tremove tremove);

    /// <summary>
    /// Legacy extension hook; unsupported in strict 9P2000 core mode unless overridden.
    /// </summary>
    /// <param name="tgetattr">The getattr request message.</param>
    /// <returns>A getattr response message.</returns>
    Task<Rgetattr> GetAttrAsync(Tgetattr tgetattr) => Task.FromException<Rgetattr>(new NinePNotSupportedException("Message type not implemented or supported"));

    /// <summary>
    /// Legacy extension hook; unsupported in strict 9P2000 core mode unless overridden.
    /// </summary>
    /// <param name="tsetattr">The setattr request message.</param>
    /// <returns>A setattr response message.</returns>
    Task<Rsetattr> SetAttrAsync(Tsetattr tsetattr) => Task.FromException<Rsetattr>(new NinePNotSupportedException("Message type not implemented or supported"));

    // Default implementations for missing 9P2000 messages
    /// <summary>
    /// Creates a new file (9P2000).
    /// </summary>
    Task<Rcreate> CreateAsync(Tcreate tcreate) => Task.FromException<Rcreate>(new NinePNotSupportedException("Message type not implemented or supported"));

    // Default implementations for missing 9P2000.L messages
    /// <summary>
    /// Retrieves filesystem statistics (9P2000.L).
    /// </summary>
    Task<Rstatfs> StatfsAsync(Tstatfs tstatfs) => Task.FromException<Rstatfs>(new NinePNotSupportedException("Message type not implemented or supported"));
    
    /// <summary>
    /// Opens a file with specific Linux flags (9P2000.L).
    /// </summary>
    Task<Rlopen> LopenAsync(Tlopen tlopen) => Task.FromException<Rlopen>(new NinePNotSupportedException("Message type not implemented or supported"));
    
    /// <summary>
    /// Creates a file with specific Linux flags (9P2000.L).
    /// </summary>
    Task<Rlcreate> LcreateAsync(Tlcreate tlcreate) => Task.FromException<Rlcreate>(new NinePNotSupportedException("Message type not implemented or supported"));
    
    /// <summary>
    /// Creates a symbolic link (9P2000.L).
    /// </summary>
    Task<Rsymlink> SymlinkAsync(Tsymlink tsymlink) => Task.FromException<Rsymlink>(new NinePNotSupportedException("Message type not implemented or supported"));
    
    /// <summary>
    /// Creates a device node or FIFO (9P2000.L).
    /// </summary>
    Task<Rmknod> MknodAsync(Tmknod tmknod) => Task.FromException<Rmknod>(new NinePNotSupportedException("Message type not implemented or supported"));
    
    /// <summary>
    /// Renames a file (9P2000.L).
    /// </summary>
    Task<Rrename> RenameAsync(Trename trename) => Task.FromException<Rrename>(new NinePNotSupportedException("Message type not implemented or supported"));
    
    /// <summary>
    /// Reads a symbolic link (9P2000.L).
    /// </summary>
    Task<Rreadlink> ReadlinkAsync(Treadlink treadlink) => Task.FromException<Rreadlink>(new NinePNotSupportedException("Message type not implemented or supported"));
    
    /// <summary>
    /// Walks to an extended attribute (9P2000.L).
    /// </summary>
    Task<Rxattrwalk> XattrwalkAsync(Txattrwalk txattrwalk) => Task.FromException<Rxattrwalk>(new NinePNotSupportedException("Message type not implemented or supported"));
    
    /// <summary>
    /// Creates an extended attribute (9P2000.L).
    /// </summary>
    Task<Rxattrcreate> XattrcreateAsync(Txattrcreate txattrcreate) => Task.FromException<Rxattrcreate>(new NinePNotSupportedException("Message type not implemented or supported"));
    
    /// <summary>
    /// Reads directory entries (9P2000.L).
    /// </summary>
    Task<Rreaddir> ReaddirAsync(Treaddir treaddir) => Task.FromException<Rreaddir>(new NinePNotSupportedException("Message type not implemented or supported"));
    
    /// <summary>
    /// Syncs file data to disk (9P2000.L).
    /// </summary>
    Task<Rfsync> FsyncAsync(Tfsync tfsync) => Task.FromException<Rfsync>(new NinePNotSupportedException("Message type not implemented or supported"));
    
    /// <summary>
    /// Acquires a lock on a file (9P2000.L).
    /// </summary>
    Task<Rlock> LockAsync(Tlock tlock) => Task.FromException<Rlock>(new NinePNotSupportedException("Message type not implemented or supported"));
    
    /// <summary>
    /// Retrieves lock information for a file (9P2000.L).
    /// </summary>
    Task<Rgetlock> GetlockAsync(Tgetlock tgetlock) => Task.FromException<Rgetlock>(new NinePNotSupportedException("Message type not implemented or supported"));
    
    /// <summary>
    /// Creates a hard link (9P2000.L).
    /// </summary>
    Task<Rlink> LinkAsync(Tlink tlink) => Task.FromException<Rlink>(new NinePNotSupportedException("Message type not implemented or supported"));
    
    /// <summary>
    /// Creates a directory (9P2000.L).
    /// </summary>
    Task<Rmkdir> MkdirAsync(Tmkdir tmkdir) => Task.FromException<Rmkdir>(new NinePNotSupportedException("Message type not implemented or supported"));
    
    /// <summary>
    /// Renames a file at a specific directory (9P2000.L).
    /// </summary>
    Task<Rrenameat> RenameatAsync(Trenameat trenameat) => Task.FromException<Rrenameat>(new NinePNotSupportedException("Message type not implemented or supported"));
    
    /// <summary>
    /// Removes a file at a specific directory (9P2000.L).
    /// </summary>
    Task<Runlinkat> UnlinkatAsync(Tunlinkat tunlinkat) => Task.FromException<Runlinkat>(new NinePNotSupportedException("Message type not implemented or supported"));

    /// <summary>
    /// Creates a shallow clone of the current filesystem state for use with a new FID.
    /// </summary>
    /// <returns>A new instance of the filesystem.</returns>
    INinePFileSystem Clone();
}
