using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Constants;
using NinePSharp.Server.Interfaces;
using System.IO;

namespace NinePSharp.Server;

internal class RootFileSystem : INinePFileSystem
{
    private readonly List<IProtocolBackend> _backends;
    private INinePFileSystem? _delegatedFs;

    public bool DotU { get; set; }

    public RootFileSystem(List<IProtocolBackend> backends)
    {
        _backends = backends;
    }

    public async Task<Rwalk> WalkAsync(Twalk twalk)
    {
        // If we are already delegating, pass the walk to the backend
        if (_delegatedFs != null) return await _delegatedFs.WalkAsync(twalk);

        if (twalk.Wname.Length == 0) return new Rwalk(twalk.Tag, Array.Empty<Qid>());

        // Try to find a backend that matches the first element of the walk
        var name = twalk.Wname[0];
        var backend = _backends.FirstOrDefault(b => b.MountPath.Trim('/') == name);

        if (backend != null)
        {
            // Enter the backend
            _delegatedFs = backend.GetFileSystem(null);
            _delegatedFs.DotU = this.DotU;

            var qid = new Qid(QidType.QTDIR, 0, (ulong)name.GetHashCode());
            var qids = new List<Qid> { qid };

            if (twalk.Wname.Length > 1)
            {
                // We have more steps! 
                // We must create a TWALK that starts from the backend root.
                // NOTE: the FID in twalk is the root fid, but the sub-walk
                // is conceptually walking relative to the new delegated FS.
                var subWalk = new Twalk(twalk.Tag, twalk.Fid, twalk.NewFid, twalk.Wname.Skip(1).ToArray());
                var subResponse = await _delegatedFs.WalkAsync(subWalk);
                
                if (subResponse.Wqid != null)
                {
                    qids.AddRange(subResponse.Wqid);
                }
            }

            return new Rwalk(twalk.Tag, qids.ToArray());
        }

        return new Rwalk(twalk.Tag, null); 
    }

    public async Task<Rread> ReadAsync(Tread tread)
    {
        if (_delegatedFs != null) return await _delegatedFs.ReadAsync(tread);

        var entries = new List<byte>();
        foreach (var backend in _backends)
        {
            var name = backend.MountPath.Trim('/');
            if (string.IsNullOrEmpty(name)) continue;
            var stat = new Stat(0, 0, 0, new Qid(QidType.QTDIR, 0, (ulong)name.GetHashCode()), 0755 | (uint)NinePConstants.FileMode9P.DMDIR, 0, 0, 0, name, "scott", "scott", "scott", dotu: DotU);
            
            var entryBuffer = new byte[stat.Size + 2];
            int offset = 0;
            stat.WriteTo(entryBuffer, ref offset);
            entries.AddRange(entryBuffer.Take(offset));
        }

        var allData = entries.ToArray();
        if (tread.Offset >= (ulong)allData.Length) return new Rread(tread.Tag, Array.Empty<byte>());
        
        var chunk = allData.AsSpan((int)tread.Offset, (int)Math.Min((long)tread.Count, (long)allData.Length - (long)tread.Offset)).ToArray();
        return new Rread(tread.Tag, chunk);
    }

    public async Task<Rstat> StatAsync(Tstat tstat)
    {
        if (_delegatedFs != null) return await _delegatedFs.StatAsync(tstat);
        var stat = new Stat(0, 0, 0, new Qid(QidType.QTDIR, 0, 0), 0755 | (uint)NinePConstants.FileMode9P.DMDIR, 0, 0, 0, "/", "scott", "scott", "scott", dotu: DotU);
        return new Rstat(tstat.Tag, stat);
    }

    public Task<Rclunk> ClunkAsync(Tclunk tclunk) => _delegatedFs?.ClunkAsync(tclunk) ?? Task.FromResult(new Rclunk(tclunk.Tag));
    
    public async Task<Ropen> OpenAsync(Topen topen)
    {
        if (_delegatedFs != null) return await _delegatedFs.OpenAsync(topen);
        return new Ropen(topen.Tag, new Qid(QidType.QTDIR, 0, 0), 0);
    }

    public Task<Rwrite> WriteAsync(Twrite twrite) => _delegatedFs?.WriteAsync(twrite) ?? throw new NotSupportedException();
    public Task<Rwstat> WstatAsync(Twstat twstat) => _delegatedFs?.WstatAsync(twstat) ?? throw new NotSupportedException();
    public Task<Rremove> RemoveAsync(Tremove tremove) => _delegatedFs?.RemoveAsync(tremove) ?? throw new NotSupportedException();

    public INinePFileSystem Clone()
    {
        var clone = new RootFileSystem(_backends) { DotU = this.DotU };
        if (_delegatedFs != null) clone._delegatedFs = _delegatedFs.Clone();
        return clone;
    }
}
