using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Constants;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Server.Backends;

public class MockFileSystem : INinePFileSystem
{
    private readonly ILuxVaultService _vault;
    private List<string> _currentPath = new();

    public bool DotU { get; set; }

    public MockFileSystem(ILuxVaultService vault)
    {
        _vault = vault;
    }

    public Task<Rwalk> WalkAsync(Twalk twalk) 
    {
        var qids = new List<Qid>();
        foreach (var name in twalk.Wname)
        {
            if (name == "..")
            {
                if (_currentPath.Count > 0) _currentPath.RemoveAt(_currentPath.Count - 1);
            }
            else
            {
                _currentPath.Add(name);
            }
            qids.Add(new Qid(QidType.QTDIR, 0, (ulong)name.GetHashCode()));
        }
        return Task.FromResult(new Rwalk(twalk.Tag, qids.ToArray())); 
    }

    public Task<Ropen> OpenAsync(Topen topen)
    {
        return Task.FromResult(new Ropen(topen.Tag, new Qid(QidType.QTFILE, 0, 0), 8192));
    }

    public Task<Rread> ReadAsync(Tread tread)
    {
        return Task.FromResult(new Rread(tread.Tag, Array.Empty<byte>()));
    }

    public Task<Rwrite> WriteAsync(Twrite twrite)
    {
        return Task.FromResult(new Rwrite(twrite.Tag, (uint)twrite.Data.Length));
    }

    public Task<Rclunk> ClunkAsync(Tclunk tclunk)
    {
        return Task.FromResult(new Rclunk(tclunk.Tag));
    }

    public Task<Rstat> StatAsync(Tstat tstat)
    {
        var name = _currentPath.Count > 0 ? _currentPath.Last() : "mock";
        var stat = new Stat(0, 0, 0, new Qid(QidType.QTFILE, 0, 1), 0644, 0, 0, 0, name, "scott", "scott", "scott", dotu: DotU);
        return Task.FromResult(new Rstat(tstat.Tag, stat));
    }

    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NotSupportedException();

    public Task<Rremove> RemoveAsync(Tremove tremove) => throw new NotSupportedException();

    public Task<Rgetattr> GetAttrAsync(Tgetattr tgetattr)
    {
        var qid = new Qid(QidType.QTDIR, 0, 0);
        ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return Task.FromResult(new NinePSharp.Messages.Rgetattr(tgetattr.Tag, (ulong)NinePConstants.GetAttrMask.P9_GETATTR_BASIC, qid, (uint)NinePConstants.FileMode9P.DMDIR | 0x1EDu, 0, 0, 1, 0, 0, 4096, 0, now, 0, now, 0, now, 0, 0, 0, 0, 0));
    }

    public Task<Rsetattr> SetAttrAsync(Tsetattr tsetattr) => throw new NotSupportedException();

    public INinePFileSystem Clone()
    {
        var clone = new MockFileSystem(_vault);
        clone._currentPath = new List<string>(_currentPath);
        clone.DotU = DotU;
        return clone;
    }
}
