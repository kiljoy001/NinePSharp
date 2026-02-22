using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Constants;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Server;

/// <summary>
/// A virtual file system for the root '/' that lists all mounted backends as directories.
/// </summary>
internal class RootFileSystem : INinePFileSystem
{
    private readonly List<IProtocolBackend> _backends;

    public bool DotU { get; set; }

    public RootFileSystem(List<IProtocolBackend> backends)
    {
        _backends = backends;
    }

    public Task<Rwalk> WalkAsync(Twalk twalk)
    {
        // Root only supports 1 step walk for now
        return Task.FromResult(new Rwalk(twalk.Tag, Array.Empty<Qid>()));
    }

    public async Task<Rread> ReadAsync(Tread tread)
    {
        // Root read returns directory entries for backends
        var entries = new List<byte>();
        foreach (var backend in _backends)
        {
            var name = backend.MountPath.Trim('/');
            if (string.IsNullOrEmpty(name)) continue;
            var stat = new Stat(0, 0, 0, new Qid(QidType.QTDIR, 0, (ulong)name.GetHashCode()), 0755 | (uint)NinePConstants.FileMode9P.DMDIR, 0, 0, 0, name, "scott", "scott", "scott", dotu: DotU);
            
            var entryBuffer = new byte[stat.Size];
            int offset = 0;
            stat.WriteTo(entryBuffer, ref offset);
            entries.AddRange(entryBuffer);
        }

        var allData = entries.ToArray();
        
        int totalToSend = 0;
        int currentOffset = (int)tread.Offset;
        while (currentOffset + 2 <= allData.Length)
        {
            ushort entrySize = (ushort)(System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(allData.AsSpan(currentOffset, 2)) + 2);
            if (totalToSend + entrySize > tread.Count) break;
            totalToSend += entrySize;
            currentOffset += entrySize;
        }
        
        if (totalToSend == 0) return new Rread(tread.Tag, Array.Empty<byte>());
        return new Rread(tread.Tag, allData.AsMemory((int)tread.Offset, totalToSend).ToArray());
    }

    public Task<Rstat> StatAsync(Tstat tstat)
    {
        var stat = new Stat(0, 0, 0, new Qid(QidType.QTDIR, 0, 0), 0755 | (uint)NinePConstants.FileMode9P.DMDIR, 0, 0, 0, "/", "scott", "scott", "scott", dotu: DotU);
        return Task.FromResult(new Rstat(tstat.Tag, stat));
    }

    public Task<Rclunk> ClunkAsync(Tclunk tclunk) => Task.FromResult(new Rclunk(tclunk.Tag));
    public Task<Ropen> OpenAsync(Topen topen) => Task.FromResult(new Ropen(topen.Tag, new Qid(QidType.QTDIR, 0, 0), 0));
    public Task<Rwrite> WriteAsync(Twrite twrite) => throw new NotSupportedException();
    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NotSupportedException();
    public Task<Rremove> RemoveAsync(Tremove tremove) => throw new NotSupportedException();

    public INinePFileSystem Clone()
    {
        return new RootFileSystem(_backends) { DotU = this.DotU };
    }
}
