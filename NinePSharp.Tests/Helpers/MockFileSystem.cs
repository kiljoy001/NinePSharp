using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

namespace NinePSharp.Tests;

internal class MockFileSystem : INinePFileSystem
{
    private enum MockEntryType
    {
        File,
        Directory
    }

    private sealed class MockEntry
    {
        public string Name { get; set; } = "";
        public MockEntryType Type { get; set; }
        public uint Mode { get; set; }
        public uint Gid { get; set; }
        public ulong Qid { get; set; }
        public byte[] Content { get; set; } = Array.Empty<byte>();
    }

    private readonly ConcurrentDictionary<string, MockEntry> _entries;
    private List<string> _currentPath = new();

    public NinePDialect Dialect { get; set; } = NinePDialect.NineP2000;

    public MockFileSystem()
        : this("mock")
    {
    }

    public MockFileSystem(string seed)
    {
        _entries = new ConcurrentDictionary<string, MockEntry>(StringComparer.Ordinal);
        _entries["/"] = new MockEntry
        {
            Name = "/",
            Type = MockEntryType.Directory,
            Mode = 0x1ED,
            Qid = 0
        };

        _entries["/data"] = new MockEntry
        {
            Name = "data",
            Type = MockEntryType.File,
            Mode = 0x1A4,
            Qid = (ulong)Math.Abs(seed.GetHashCode()),
            Content = Encoding.UTF8.GetBytes(seed)
        };
    }

    public MockFileSystem(ILuxVaultService _)
        : this("mock")
    {
    }

    private string GetFullPath()
        => _currentPath.Count == 0 ? "/" : "/" + string.Join("/", _currentPath);

    public Task<Rwalk> WalkAsync(Twalk twalk)
    {
        var qids = new List<Qid>();
        var tempPath = new List<string>(_currentPath);

        foreach (string name in twalk.Wname)
        {
            if (name == "..")
            {
                if (tempPath.Count > 0)
                {
                    tempPath.RemoveAt(tempPath.Count - 1);
                }
            }
            else if (name != ".")
            {
                tempPath.Add(name);
            }

            string checkPath = tempPath.Count == 0 ? "/" : "/" + string.Join("/", tempPath);
            if (_entries.TryGetValue(checkPath, out MockEntry? entry))
            {
                QidType qidType = entry.Type == MockEntryType.Directory ? QidType.QTDIR : QidType.QTFILE;
                qids.Add(new Qid(qidType, 0, entry.Qid));
            }
            else
            {
                qids.Add(new Qid(QidType.QTDIR, 0, (ulong)Math.Abs(checkPath.GetHashCode())));
            }
        }

        if (qids.Count == twalk.Wname.Length)
        {
            _currentPath = tempPath;
        }

        return Task.FromResult(new Rwalk(twalk.Tag, qids.ToArray()));
    }

    public Task<Ropen> OpenAsync(Topen topen)
    {
        string currentPath = GetFullPath();
        bool isDir = _entries.TryGetValue(currentPath, out MockEntry? entry) && entry.Type == MockEntryType.Directory;
        QidType qidType = isDir ? QidType.QTDIR : QidType.QTFILE;
        return Task.FromResult(new Ropen(topen.Tag, new Qid(qidType, 0, 0), 8192));
    }

    public Task<Rread> ReadAsync(Tread tread)
    {
        string currentPath = GetFullPath();

        if (_entries.TryGetValue(currentPath, out MockEntry? entry))
        {
            if (entry.Type == MockEntryType.File)
            {
                if (tread.Offset >= (ulong)entry.Content.Length)
                {
                    return Task.FromResult(new Rread(tread.Tag, Array.Empty<byte>()));
                }

                int offset = (int)tread.Offset;
                int count = (int)Math.Min(tread.Count, (uint)(entry.Content.Length - offset));
                byte[] data = new byte[count];
                Array.Copy(entry.Content, offset, data, 0, count);
                return Task.FromResult(new Rread(tread.Tag, data));
            }
            else if (entry.Type == MockEntryType.Directory)
            {
                var children = _entries
                    .Where(e =>
                    {
                        string parentPath = Path.GetDirectoryName(e.Key)?.Replace('\\', '/') ?? "/";
                        return parentPath == currentPath && e.Key != currentPath;
                    })
                    .OrderBy(e => e.Key, StringComparer.Ordinal)
                    .ToList();

                var allStats = new List<byte>();
                foreach (var child in children)
                {
                    var childEntry = child.Value;
                    var qidType = childEntry.Type == MockEntryType.Directory ? QidType.QTDIR : QidType.QTFILE;
                    var mode = childEntry.Mode;
                    if (childEntry.Type == MockEntryType.Directory) mode |= (uint)NinePConstants.FileMode9P.DMDIR;
                    
                    var stat = new Stat(0, 0, 0, new Qid(qidType, 0, childEntry.Qid), mode, 0, 0, (ulong)childEntry.Content.Length, childEntry.Name, "none", "none", "none", dialect: Dialect);
                    var buffer = new byte[stat.Size];
                    int off = 0;
                    stat.WriteTo(buffer, ref off);
                    allStats.AddRange(buffer);
                }

                if (tread.Offset >= (ulong)allStats.Count)
                {
                    return Task.FromResult(new Rread(tread.Tag, Array.Empty<byte>()));
                }

                int start = (int)tread.Offset;
                int len = (int)Math.Min(tread.Count, (uint)(allStats.Count - start));
                return Task.FromResult(new Rread(tread.Tag, allStats.GetRange(start, len).ToArray()));
            }
        }

        return Task.FromResult(new Rread(tread.Tag, Array.Empty<byte>()));
    }

    public Task<Rwrite> WriteAsync(Twrite twrite)
    {
        string currentPath = GetFullPath();

        if (_entries.TryGetValue(currentPath, out MockEntry? entry) && entry.Type == MockEntryType.File)
        {
            int offset = (int)twrite.Offset;
            byte[] data = twrite.Data.ToArray();
            int newLength = Math.Max(entry.Content.Length, offset + data.Length);
            byte[] newContent = new byte[newLength];
            Array.Copy(entry.Content, newContent, entry.Content.Length);
            Array.Copy(data, 0, newContent, offset, data.Length);
            entry.Content = newContent;
            return Task.FromResult(new Rwrite(twrite.Tag, (uint)data.Length));
        }

        return Task.FromResult(new Rwrite(twrite.Tag, (uint)twrite.Data.Length));
    }

    public Task<Rclunk> ClunkAsync(Tclunk tclunk)
        => Task.FromResult(new Rclunk(tclunk.Tag));

    public Task<Rstat> StatAsync(Tstat tstat)
    {
        string currentPath = GetFullPath();
        if (_entries.TryGetValue(currentPath, out MockEntry? entry))
        {
            QidType qidType = entry.Type == MockEntryType.Directory ? QidType.QTDIR : QidType.QTFILE;
            uint mode = entry.Mode;
            if (entry.Type == MockEntryType.Directory)
            {
                mode |= (uint)NinePConstants.FileMode9P.DMDIR;
            }

            return Task.FromResult(new Rstat(
                tstat.Tag,
                new Stat(0, 0, 0, new Qid(qidType, 0, entry.Qid), mode, 0, 0, (ulong)entry.Content.Length, entry.Name, "none", "none", "none", dialect: Dialect)));
        }

        return Task.FromResult(new Rstat(
            tstat.Tag,
            new Stat(0, 0, 0, new Qid(QidType.QTFILE, 0, 1), 0x1A4, 0, 0, 0, "mock", "none", "none", "none", dialect: Dialect)));
    }

    public Task<Rwstat> WstatAsync(Twstat twstat)
    {
        string currentPath = GetFullPath();
        if (!_entries.TryGetValue(currentPath, out MockEntry? entry))
        {
            throw new NinePProtocolException("File not found");
        }

        if (twstat.Stat.Mode != 0xFFFFFFFF)
        {
            entry.Mode = twstat.Stat.Mode;
        }

        return Task.FromResult(new Rwstat(twstat.Tag));
    }

    public Task<Rremove> RemoveAsync(Tremove tremove)
    {
        string currentPath = GetFullPath();
        if (currentPath == "/")
        {
            throw new NinePProtocolException("Cannot remove root");
        }

        if (_entries.TryRemove(currentPath, out _))
        {
            return Task.FromResult(new Rremove(tremove.Tag));
        }

        throw new NinePProtocolException("File not found");
    }

    public Task<Rcreate> CreateAsync(Tcreate tcreate)
    {
        string parentPath = GetFullPath();
        string newPath = parentPath == "/" ? "/" + tcreate.Name : parentPath + "/" + tcreate.Name;

        if (!_entries.TryGetValue(parentPath, out MockEntry? parent) || parent.Type != MockEntryType.Directory)
        {
            throw new NinePProtocolException("Parent directory does not exist");
        }

        ulong qid = (ulong)Math.Abs(newPath.GetHashCode());
        _entries[newPath] = new MockEntry
        {
            Name = tcreate.Name,
            Type = (tcreate.Perm & (uint)NinePConstants.FileMode9P.DMDIR) != 0 ? MockEntryType.Directory : MockEntryType.File,
            Mode = tcreate.Perm,
            Qid = qid
        };

        _currentPath.Add(tcreate.Name);
        return Task.FromResult(new Rcreate(tcreate.Tag, new Qid(QidType.QTFILE, 0, qid), 8192));
    }

    public INinePFileSystem Clone()
    {
        var clone = new MockFileSystem();
        clone._currentPath = new List<string>(_currentPath);
        clone.Dialect = Dialect;
        clone._entries.Clear();

        foreach (var kvp in _entries)
        {
            clone._entries[kvp.Key] = new MockEntry
            {
                Name = kvp.Value.Name,
                Type = kvp.Value.Type,
                Mode = kvp.Value.Mode,
                Gid = kvp.Value.Gid,
                Qid = kvp.Value.Qid,
                Content = (byte[])kvp.Value.Content.Clone()
            };
        }

        return clone;
    }
}
