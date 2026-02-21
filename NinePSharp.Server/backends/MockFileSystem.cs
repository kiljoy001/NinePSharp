using System;
using System.Threading.Tasks;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Constants;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Server.Backends;

public class MockFileSystem : INinePFileSystem
{
    private readonly ILuxVaultService _vault;

    public MockFileSystem(ILuxVaultService vault)
    {
        _vault = vault;
    }

    public Task<Rwalk> WalkAsync(Twalk twalk) 
    {
        Console.WriteLine($"[Mock FS] Walk: NewFid={twalk.NewFid}, Path={string.Join("/", twalk.Wname)}");
        return Task.FromResult(new Rwalk(twalk.Tag, Array.Empty<Qid>())); 
    }

    public Task<Ropen> OpenAsync(Topen topen)
    {
        Console.WriteLine($"[Mock FS] Open: Fid={topen.Fid}, Mode={topen.Mode}");
        return Task.FromResult(new Ropen(topen.Tag, new Qid(QidType.QTFILE, 0, 0), 8192));
    }

    public Task<Rread> ReadAsync(Tread tread)
    {
        Console.WriteLine($"[Mock FS] Read: Fid={tread.Fid}, Offset={tread.Offset}, Count={tread.Count}");
        return Task.FromResult(new Rread(tread.Tag, Array.Empty<byte>()));
    }

    public Task<Rwrite> WriteAsync(Twrite twrite)
    {
        Console.WriteLine($"[Mock FS] Write: Fid={twrite.Fid}, Offset={twrite.Offset}, DataSize={twrite.Data.Length}");
        return Task.FromResult(new Rwrite(twrite.Tag, (uint)twrite.Data.Length));
    }

    public Task<Rclunk> ClunkAsync(Tclunk tclunk)
    {
        Console.WriteLine($"[Mock FS] Clunk: Fid={tclunk.Fid}");
        return Task.FromResult(new Rclunk(tclunk.Tag));
    }

    public Task<Rstat> StatAsync(Tstat tstat)
    {
        Console.WriteLine($"[Mock FS] Stat: Fid={tstat.Fid}");
        var stat = new Stat(0, 0, 0, new Qid(QidType.QTFILE, 0, 1), 0644, 0, 0, 0, "mockfile", "scott", "scott", "scott");
        return Task.FromResult(new Rstat(tstat.Tag, stat));
    }

    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NotSupportedException();

    public Task<Rremove> RemoveAsync(Tremove tremove) => throw new NotSupportedException();

    public INinePFileSystem Clone()
    {
        return new MockFileSystem(_vault);
    }
}
