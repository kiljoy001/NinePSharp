using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Constants;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using NinePSharp.Parser;

namespace NinePSharp.Server.Backends;

public class SrvFileSystem : INinePFileSystem
{
    private class SrvEntry : IDisposable
    {
        public byte[] Data { get; set; }

        public SrvEntry(byte[] data)
        {
            Data = data;
        }

        public void Dispose()
        {
            if (Data != null)
            {
                unsafe {
                    fixed (byte* p = Data) {
                        MemoryLock.Unlock((IntPtr)p, (nuint)Data.Length);
                    }
                }
                Array.Clear(Data);
            }
        }
    }

    private static readonly ConcurrentDictionary<string, SrvEntry> _pipes = new();
    private List<string> _currentPath = new();

    public NinePDialect Dialect { get; set; }

    public async Task<Rwalk> WalkAsync(Twalk twalk)
    {
        if (twalk.Wname.Length == 0) return new Rwalk(twalk.Tag, Array.Empty<Qid>());

        var qids = new List<Qid>();
        var tempPath = new List<string>(_currentPath);

        foreach (var name in twalk.Wname)
        {
            if (name == "..") { if (tempPath.Count > 0) tempPath.RemoveAt(tempPath.Count - 1); }
            else tempPath.Add(name);
            
            bool isDir = tempPath.Count == 0;
            qids.Add(new Qid(isDir ? QidType.QTDIR : QidType.QTFILE, 0, (ulong)string.Join("/", tempPath).GetHashCode()));
        }

        if (qids.Count == twalk.Wname.Length) _currentPath = tempPath;
        return new Rwalk(twalk.Tag, qids.ToArray());
    }

    public Task<Ropen> OpenAsync(Topen topen)
    {
        var type = _currentPath.Count == 0 ? QidType.QTDIR : QidType.QTFILE;
        return Task.FromResult(new Ropen(topen.Tag, new Qid(type, 0, 0), 0));
    }

    public Task<Rread> ReadAsync(Tread tread)
    {
        byte[] allData;

        if (_currentPath.Count == 0)
        {
            var entries = new List<byte>();
            foreach (var name in _pipes.Keys)
            {
                var qid = new Qid(QidType.QTFILE, 0, (ulong)name.GetHashCode());
                var stat = new Stat(0, 0, 0, qid, 0666, 0, 0, 0, name, "scott", "scott", "scott", dialect: Dialect);
                
                var entryBuffer = new byte[stat.Size];
                int offset = 0;
                stat.WriteTo(entryBuffer, ref offset);
                entries.AddRange(entryBuffer);
            }
            allData = entries.ToArray();

            int totalToSend = 0;
            int currentOffset = (int)tread.Offset;
            while (currentOffset + 2 <= allData.Length)
            {
                ushort entrySize = (ushort)(System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(allData.AsSpan(currentOffset, 2)) + 2);
                if (totalToSend + entrySize > tread.Count) break;
                totalToSend += entrySize;
                currentOffset += entrySize;
            }
            
            if (totalToSend == 0) return Task.FromResult(new Rread(tread.Tag, Array.Empty<byte>()));
            return Task.FromResult(new Rread(tread.Tag, allData.AsMemory((int)tread.Offset, totalToSend).ToArray()));
        }
        else
        {
            var name = _currentPath[0];
            if (_pipes.TryGetValue(name, out var entry))
            {
                allData = entry.Data;
            }
            else
            {
                allData = Array.Empty<byte>();
            }
        }

        if (tread.Offset >= (ulong)allData.Length) return Task.FromResult(new Rread(tread.Tag, Array.Empty<byte>()));
        var chunk = allData.AsSpan((int)tread.Offset, (int)Math.Min((long)tread.Count, (long)allData.Length - (long)tread.Offset)).ToArray();
        return Task.FromResult(new Rread(tread.Tag, chunk));
    }

    public Task<Rwrite> WriteAsync(Twrite twrite)
    {
        if (_currentPath.Count == 0) throw new NinePProtocolException("Cannot write to srv root.");

        var name = _currentPath[0];
        var data = twrite.Data.ToArray();

        byte[] pinnedData = GC.AllocateArray<byte>(data.Length, pinned: true);
        data.CopyTo(pinnedData, 0);
        Array.Clear(data);

        unsafe {
            fixed (byte* p = pinnedData) {
                MemoryLock.Lock((IntPtr)p, (nuint)pinnedData.Length);
            }
        }

        var newEntry = new SrvEntry(pinnedData);
        _pipes.AddOrUpdate(name, newEntry, (key, old) => {
            old.Dispose();
            return newEntry;
        });

        return Task.FromResult(new Rwrite(twrite.Tag, (uint)twrite.Data.Length));
    }

    public Task<Rclunk> ClunkAsync(Tclunk tclunk) => Task.FromResult(new Rclunk(tclunk.Tag));

    public async Task<Rstat> StatAsync(Tstat tstat)
    {
        var name = _currentPath.LastOrDefault() ?? "srv";
        bool isDir = _currentPath.Count == 0;
        var stat = new Stat(0, 0, 0, new Qid(isDir ? QidType.QTDIR : QidType.QTFILE, 0, 0), 0666 | (isDir ? (uint)NinePConstants.FileMode9P.DMDIR : 0), 0, 0, 0, name, "scott", "scott", "scott", dialect: Dialect);
        return new Rstat(tstat.Tag, stat);
    }

    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NinePNotSupportedException();
    
    public Task<Rremove> RemoveAsync(Tremove tremove) 
    {
        if (_currentPath.Count > 0 && _pipes.TryRemove(_currentPath[0], out var entry))
        {
            entry.Dispose();
        }
        return Task.FromResult(new Rremove(tremove.Tag));
    }

    public Task<Rgetattr> GetAttrAsync(Tgetattr tgetattr)
    {
        bool isDir = _currentPath.Count == 0;
        var qid = new Qid(isDir ? QidType.QTDIR : QidType.QTFILE, 0, DeterministicHash.GetStableHash64(string.Join("/", _currentPath)));
        uint mode = isDir ? (uint)NinePConstants.FileMode9P.DMDIR | 0755 : 0666;
        return Task.FromResult(new NinePSharp.Messages.Rgetattr(tgetattr.Tag, (ulong)NinePConstants.GetAttrMask.P9_GETATTR_BASIC, qid, mode));
    }

    public Task<Rsetattr> SetAttrAsync(Tsetattr tsetattr) => throw new NinePNotSupportedException();

    public INinePFileSystem Clone()
    {
        var clone = new SrvFileSystem();
        clone._currentPath = new List<string>(_currentPath);
        clone.Dialect = Dialect;
        return clone;
    }
}

public class SrvBackend : IProtocolBackend
{
    public string Name => "srv";
    public string MountPath => "/srv";

    public Task InitializeAsync(IConfiguration configuration) => Task.CompletedTask;
    public INinePFileSystem GetFileSystem(X509Certificate2? certificate = null) => new SrvFileSystem();
    public INinePFileSystem GetFileSystem(SecureString? credentials, X509Certificate2? certificate = null) => GetFileSystem(certificate);
}
