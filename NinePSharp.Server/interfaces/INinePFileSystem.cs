using System.Threading.Tasks;
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
    INinePFileSystem Clone();
}
