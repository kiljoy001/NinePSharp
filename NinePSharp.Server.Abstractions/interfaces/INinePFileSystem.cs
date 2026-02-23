using System.Threading.Tasks;
using NinePSharp.Server.Utils;
using NinePSharp.Messages;

namespace NinePSharp.Server.Interfaces;

public interface INinePFileSystem
{
    bool DotU { get; set; }
    Task<Rwalk> WalkAsync(Twalk twalk);
    Task<Ropen> OpenAsync(Topen topen);
    Task<Rread> ReadAsync(Tread tread);
    Task<Rwrite> WriteAsync(Twrite twrite);
    Task<Rclunk> ClunkAsync(Tclunk tclunk);
    Task<Rstat> StatAsync(Tstat tstat);
    Task<Rwstat> WstatAsync(Twstat twstat);
    Task<Rremove> RemoveAsync(Tremove tremove);
    Task<Rgetattr> GetAttrAsync(Tgetattr tgetattr);
    Task<Rsetattr> SetAttrAsync(Tsetattr tsetattr);

    // Default implementations for missing 9P2000 messages
    Task<Rcreate> CreateAsync(Tcreate tcreate) => Task.FromException<Rcreate>(new NinePProtocolException("Message type not implemented or supported"));

    // Default implementations for missing 9P2000.L messages
    Task<Rstatfs> StatfsAsync(Tstatfs tstatfs) => Task.FromException<Rstatfs>(new NinePProtocolException("Message type not implemented or supported"));
    Task<Rlopen> LopenAsync(Tlopen tlopen) => Task.FromException<Rlopen>(new NinePProtocolException("Message type not implemented or supported"));
    Task<Rlcreate> LcreateAsync(Tlcreate tlcreate) => Task.FromException<Rlcreate>(new NinePProtocolException("Message type not implemented or supported"));
    Task<Rsymlink> SymlinkAsync(Tsymlink tsymlink) => Task.FromException<Rsymlink>(new NinePProtocolException("Message type not implemented or supported"));
    Task<Rmknod> MknodAsync(Tmknod tmknod) => Task.FromException<Rmknod>(new NinePProtocolException("Message type not implemented or supported"));
    Task<Rrename> RenameAsync(Trename trename) => Task.FromException<Rrename>(new NinePProtocolException("Message type not implemented or supported"));
    Task<Rreadlink> ReadlinkAsync(Treadlink treadlink) => Task.FromException<Rreadlink>(new NinePProtocolException("Message type not implemented or supported"));
    Task<Rxattrwalk> XattrwalkAsync(Txattrwalk txattrwalk) => Task.FromException<Rxattrwalk>(new NinePProtocolException("Message type not implemented or supported"));
    Task<Rxattrcreate> XattrcreateAsync(Txattrcreate txattrcreate) => Task.FromException<Rxattrcreate>(new NinePProtocolException("Message type not implemented or supported"));
    Task<Rreaddir> ReaddirAsync(Treaddir treaddir) => Task.FromException<Rreaddir>(new NinePProtocolException("Message type not implemented or supported"));
    Task<Rfsync> FsyncAsync(Tfsync tfsync) => Task.FromException<Rfsync>(new NinePProtocolException("Message type not implemented or supported"));
    Task<Rlock> LockAsync(Tlock tlock) => Task.FromException<Rlock>(new NinePProtocolException("Message type not implemented or supported"));
    Task<Rgetlock> GetlockAsync(Tgetlock tgetlock) => Task.FromException<Rgetlock>(new NinePProtocolException("Message type not implemented or supported"));
    Task<Rlink> LinkAsync(Tlink tlink) => Task.FromException<Rlink>(new NinePProtocolException("Message type not implemented or supported"));
    Task<Rmkdir> MkdirAsync(Tmkdir tmkdir) => Task.FromException<Rmkdir>(new NinePProtocolException("Message type not implemented or supported"));
    Task<Rrenameat> RenameatAsync(Trenameat trenameat) => Task.FromException<Rrenameat>(new NinePProtocolException("Message type not implemented or supported"));
    Task<Runlinkat> UnlinkatAsync(Tunlinkat tunlinkat) => Task.FromException<Runlinkat>(new NinePProtocolException("Message type not implemented or supported"));

    INinePFileSystem Clone();
}
