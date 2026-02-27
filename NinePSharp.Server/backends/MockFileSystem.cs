using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Constants;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Backends;

public class MockFileSystem : INinePFileSystem
{
    private enum MockEntryType { File, Directory }

    private class MockEntry
    {
        public string Name { get; set; } = "";
        public MockEntryType Type { get; set; }
        public uint Mode { get; set; }
        public uint Gid { get; set; }
        public ulong Qid { get; set; }
        public byte[] Content { get; set; } = Array.Empty<byte>();
        public DateTime Created { get; set; } = DateTime.UtcNow;
    }

    private readonly ILuxVaultService _vault;
    private readonly ConcurrentDictionary<string, MockEntry> _entries;
    private List<string> _currentPath = new();

    public NinePDialect Dialect { get; set; }

    public MockFileSystem(ILuxVaultService vault)
    {
        _vault = vault;
        _entries = new ConcurrentDictionary<string, MockEntry>();

        // Initialize with root directory
        _entries["/"] = new MockEntry
        {
            Name = "/",
            Type = MockEntryType.Directory,
            Mode = 0755,
            Qid = 0
        };
    }

    private string GetFullPath()
    {
        if (_currentPath.Count == 0) return "/";
        return "/" + string.Join("/", _currentPath);
    }

    public Task<Rwalk> WalkAsync(Twalk twalk)
    {
        var qids = new List<Qid>();
        var tempPath = new List<string>(_currentPath);

        foreach (var name in twalk.Wname)
        {
            if (name == "..")
            {
                if (tempPath.Count > 0) tempPath.RemoveAt(tempPath.Count - 1);
            }
            else
            {
                tempPath.Add(name);
            }

            // Check if path exists
            var checkPath = tempPath.Count == 0 ? "/" : "/" + string.Join("/", tempPath);
            if (_entries.TryGetValue(checkPath, out var entry))
            {
                var qidType = entry.Type == MockEntryType.Directory ? QidType.QTDIR : QidType.QTFILE;
                qids.Add(new Qid(qidType, 0, entry.Qid));
            }
            else
            {
                // Path doesn't exist, but we'll still add a QID (for compatibility)
                qids.Add(new Qid(QidType.QTDIR, 0, (ulong)name.GetHashCode()));
            }
        }

        // Only update current path if all walks succeeded
        if (qids.Count == twalk.Wname.Length)
        {
            _currentPath = tempPath;
        }

        return Task.FromResult(new Rwalk(twalk.Tag, qids.ToArray()));
    }

    public Task<Ropen> OpenAsync(Topen topen)
    {
        return Task.FromResult(new Ropen(topen.Tag, new Qid(QidType.QTFILE, 0, 0), 8192));
    }

    public Task<Rread> ReadAsync(Tread tread)
    {
        var currentPath = GetFullPath();

        if (_entries.TryGetValue(currentPath, out var entry) && entry.Type == MockEntryType.File)
        {
            // Read from file content
            if (tread.Offset >= (ulong)entry.Content.Length)
            {
                return Task.FromResult(new Rread(tread.Tag, Array.Empty<byte>()));
            }

            var offset = (int)tread.Offset;
            var count = (int)Math.Min(tread.Count, (uint)(entry.Content.Length - offset));
            var data = new byte[count];
            Array.Copy(entry.Content, offset, data, 0, count);

            return Task.FromResult(new Rread(tread.Tag, data));
        }

        return Task.FromResult(new Rread(tread.Tag, Array.Empty<byte>()));
    }

    public Task<Rwrite> WriteAsync(Twrite twrite)
    {
        var currentPath = GetFullPath();

        if (_entries.TryGetValue(currentPath, out var entry) && entry.Type == MockEntryType.File)
        {
            // Append to or overwrite file content
            var offset = (int)twrite.Offset;
            var data = twrite.Data.ToArray();

            if (offset >= entry.Content.Length)
            {
                // Append
                var newContent = new byte[offset + data.Length];
                Array.Copy(entry.Content, newContent, entry.Content.Length);
                Array.Copy(data, 0, newContent, offset, data.Length);
                entry.Content = newContent;
            }
            else
            {
                // Overwrite
                var newLength = Math.Max(entry.Content.Length, offset + data.Length);
                var newContent = new byte[newLength];
                Array.Copy(entry.Content, newContent, entry.Content.Length);
                Array.Copy(data, 0, newContent, offset, data.Length);
                entry.Content = newContent;
            }

            return Task.FromResult(new Rwrite(twrite.Tag, (uint)data.Length));
        }

        return Task.FromResult(new Rwrite(twrite.Tag, (uint)twrite.Data.Length));
    }

    public Task<Rclunk> ClunkAsync(Tclunk tclunk)
    {
        return Task.FromResult(new Rclunk(tclunk.Tag));
    }

    public Task<Rstat> StatAsync(Tstat tstat)
    {
        var currentPath = GetFullPath();

        if (_entries.TryGetValue(currentPath, out var entry))
        {
            var qidType = entry.Type == MockEntryType.Directory ? QidType.QTDIR : QidType.QTFILE;
            var mode = entry.Mode;
            if (entry.Type == MockEntryType.Directory)
            {
                mode |= (uint)NinePConstants.FileMode9P.DMDIR;
            }

            var stat = new Stat(0, 0, 0, new Qid(qidType, 0, entry.Qid), mode, 0, 0,
                (ulong)entry.Content.Length, entry.Name, "scott", "scott", "scott", dialect: Dialect);
            return Task.FromResult(new Rstat(tstat.Tag, stat));
        }
        else
        {
            // Fallback for non-existent paths
            var name = _currentPath.Count > 0 ? _currentPath.Last() : "mock";
            var stat = new Stat(0, 0, 0, new Qid(QidType.QTFILE, 0, 1), 0644, 0, 0, 0, name, "scott", "scott", "scott", dialect: Dialect);
            return Task.FromResult(new Rstat(tstat.Tag, stat));
        }
    }

    public Task<Rwstat> WstatAsync(Twstat twstat)
    {
        var currentPath = GetFullPath();
        if (_entries.TryGetValue(currentPath, out var entry))
        {
            // Only update mode for now as a stub for actual implementation
            if (twstat.Stat.Mode != 0xFFFFFFFF)
            {
                entry.Mode = twstat.Stat.Mode;
            }
            return Task.FromResult(new Rwstat(twstat.Tag));
        }
        throw new NinePProtocolException("File not found");
    }

    public Task<Rremove> RemoveAsync(Tremove tremove)
    {
        var currentPath = GetFullPath();
        if (currentPath == "/") throw new NinePProtocolException("Cannot remove root");

        if (_entries.TryRemove(currentPath, out _))
        {
            return Task.FromResult(new Rremove(tremove.Tag));
        }
        throw new NinePProtocolException("File not found");
    }

    public Task<Rgetattr> GetAttrAsync(Tgetattr tgetattr)
    {
        var currentPath = GetFullPath();
        if (_entries.TryGetValue(currentPath, out var entry))
        {
            var qidType = entry.Type == MockEntryType.Directory ? QidType.QTDIR : QidType.QTFILE;
            var qid = new Qid(qidType, 0, entry.Qid);
            var mode = entry.Mode;
            if (entry.Type == MockEntryType.Directory) mode |= (uint)NinePConstants.FileMode9P.DMDIR;
            
            return Task.FromResult(new NinePSharp.Messages.Rgetattr(tgetattr.Tag, (ulong)NinePConstants.GetAttrMask.P9_GETATTR_BASIC, qid, mode));
        }
        
        var fallbackQid = new Qid(QidType.QTFILE, 0, 1);
        return Task.FromResult(new NinePSharp.Messages.Rgetattr(tgetattr.Tag, (ulong)NinePConstants.GetAttrMask.P9_GETATTR_BASIC, fallbackQid, 0644));
    }

    public Task<Rsetattr> SetAttrAsync(Tsetattr tsetattr) => throw new NinePNotSupportedException();

    public Task<Rstatfs> StatfsAsync(Tstatfs tstatfs)
    {
        // Calculate dynamic statistics based on actual entries
        var fileCount = _entries.Count(e => e.Value.Type == MockEntryType.File);
        var dirCount = _entries.Count(e => e.Value.Type == MockEntryType.Directory);
        var totalBlocks = 100000UL;
        var usedBlocks = (ulong)(_entries.Sum(e => e.Value.Content.Length) / 4096);

        return Task.FromResult(new Rstatfs(tstatfs.Tag,
            0x01021997,  // fstype (arbitrary)
            4096,        // block size
            totalBlocks, // total blocks
            totalBlocks - usedBlocks, // free blocks
            totalBlocks - usedBlocks, // available blocks
            (ulong)(fileCount + dirCount), // total files
            totalBlocks - (ulong)_entries.Count, // free files (capacity - used)
            0,           // fsid
            256));       // max name length
    }

    public Task<Rmkdir> MkdirAsync(Tmkdir tmkdir)
    {
        var parentPath = GetFullPath();
        var newPath = parentPath == "/" ? "/" + tmkdir.Name : parentPath + "/" + tmkdir.Name;

        // Check if parent exists and is a directory
        if (!_entries.TryGetValue(parentPath, out var parent) || parent.Type != MockEntryType.Directory)
        {
            throw new NinePProtocolException("Parent directory does not exist");
        }

        // Check if already exists
        if (_entries.ContainsKey(newPath))
        {
            throw new NinePProtocolException("Directory already exists");
        }

        // Create new directory
        var qid = (ulong)(newPath.GetHashCode() & 0x7FFFFFFF);
        var entry = new MockEntry
        {
            Name = tmkdir.Name,
            Type = MockEntryType.Directory,
            Mode = tmkdir.Mode,
            Gid = tmkdir.Gid,
            Qid = qid
        };

        _entries[newPath] = entry;

        return Task.FromResult(new Rmkdir(
            NinePConstants.HeaderSize + 13,
            tmkdir.Tag,
            new Qid(QidType.QTDIR, 0, qid)));
    }

    public Task<Rcreate> CreateAsync(Tcreate tcreate)
    {
        // 9P2000 create - creates a file in the current directory
        var parentPath = GetFullPath();
        var newPath = parentPath == "/" ? "/" + tcreate.Name : parentPath + "/" + tcreate.Name;

        // Check if parent exists and is a directory
        if (!_entries.TryGetValue(parentPath, out var parent) || parent.Type != MockEntryType.Directory)
        {
            throw new NinePProtocolException("Parent directory does not exist");
        }

        // Check permissions (if DMDIR is set, must fail - Tcreate can only create files)
        if ((tcreate.Perm & (uint)NinePConstants.FileMode9P.DMDIR) != 0)
        {
            throw new NinePProtocolException("Cannot create directory with Tcreate, use Tmkdir");
        }

        // Create new file
        var qid = (ulong)(newPath.GetHashCode() & 0x7FFFFFFF);
        var entry = new MockEntry
        {
            Name = tcreate.Name,
            Type = MockEntryType.File,
            Mode = tcreate.Perm,
            Qid = qid
        };

        _entries[newPath] = entry;
        _currentPath.Add(tcreate.Name); // Tcreate implicitly opens the file

        return Task.FromResult(new Rcreate(tcreate.Tag, new Qid(QidType.QTFILE, 0, qid), 8192));
    }

    public Task<Rlcreate> LcreateAsync(Tlcreate tlcreate)
    {
        // 9P2000.L create - creates a file in the current directory
        var parentPath = GetFullPath();
        var newPath = parentPath == "/" ? "/" + tlcreate.Name : parentPath + "/" + tlcreate.Name;

        // Check if parent exists and is a directory
        if (!_entries.TryGetValue(parentPath, out var parent) || parent.Type != MockEntryType.Directory)
        {
            throw new NinePProtocolException("Parent directory does not exist");
        }

        // Create new file
        var qid = (ulong)(newPath.GetHashCode() & 0x7FFFFFFF);
        var entry = new MockEntry
        {
            Name = tlcreate.Name,
            Type = MockEntryType.File,
            Mode = tlcreate.Mode,
            Gid = tlcreate.Gid,
            Qid = qid
        };

        _entries[newPath] = entry;

        return Task.FromResult(new Rlcreate(
            NinePConstants.HeaderSize + 13 + 4,
            tlcreate.Tag,
            new Qid(QidType.QTFILE, 0, qid),
            8192));
    }

    public Task<Rrename> RenameAsync(Trename trename)
    {
        // Note: Trename uses FIDs, not paths. In our simple mock, we'll use current path.
        var oldPath = GetFullPath();

        // Find destination directory
        // In a real implementation, dfid would be tracked in FID table
        // For mock: assume dfid refers to a directory path we need to compute
        var newPath = "/" + trename.Name; // Simplified for mock

        if (!_entries.TryGetValue(oldPath, out var entry))
        {
            throw new NinePProtocolException("Source file does not exist");
        }

        // Remove from old location, add to new
        _entries.TryRemove(oldPath, out _);
        entry.Name = trename.Name;
        _entries[newPath] = entry;

        return Task.FromResult(new Rrename(NinePConstants.HeaderSize, trename.Tag));
    }

    public Task<Rrenameat> RenameatAsync(Trenameat trenameat)
    {
        // Simplified for mock - compute paths from names
        var oldPath = "/" + trenameat.OldName;
        var newPath = "/" + trenameat.NewName;

        if (!_entries.TryGetValue(oldPath, out var entry))
        {
            throw new NinePProtocolException("Source file does not exist");
        }

        // Remove from old location, add to new
        _entries.TryRemove(oldPath, out _);
        entry.Name = trenameat.NewName;
        _entries[newPath] = entry;

        return Task.FromResult(new Rrenameat(NinePConstants.HeaderSize, trenameat.Tag));
    }

    public Task<Rreaddir> ReaddirAsync(Treaddir treaddir)
    {
        var currentPath = GetFullPath();

        // Check if current path is a directory
        if (!_entries.TryGetValue(currentPath, out var dir) || dir.Type != MockEntryType.Directory)
        {
            throw new NinePProtocolException("Not a directory");
        }

        // Find all children of current directory
        var children = _entries
            .Where(e => {
                var parentPath = Path.GetDirectoryName(e.Key)?.Replace('\\', '/') ?? "/";
                return parentPath == currentPath && e.Key != currentPath;
            })
            .OrderBy(e => e.Key)
            .ToList();

        var entries = new List<byte>();
        ulong offset = 0;

        foreach (var child in children)
        {
            offset++;
            if (offset <= treaddir.Offset) continue; // Skip already-read entries

            var childEntry = child.Value;
            var qidType = childEntry.Type == MockEntryType.Directory ? QidType.QTDIR : QidType.QTFILE;
            var qid = new Qid(qidType, 0, childEntry.Qid);

            // Format: [qid[13] offset[8] type[1] name[s]]
            int nameLen = Encoding.UTF8.GetByteCount(childEntry.Name);
            int entrySize = 13 + 8 + 1 + 2 + nameLen;
            var entryBuffer = new byte[entrySize];

            int writeOffset = 0;
            // Write Qid (13 bytes)
            entryBuffer[writeOffset++] = (byte)qid.Type;
            BinaryPrimitives.WriteUInt32LittleEndian(entryBuffer.AsSpan(writeOffset, 4), qid.Version);
            writeOffset += 4;
            BinaryPrimitives.WriteUInt64LittleEndian(entryBuffer.AsSpan(writeOffset, 8), qid.Path);
            writeOffset += 8;

            // Write Offset (8 bytes) - next offset to read from
            BinaryPrimitives.WriteUInt64LittleEndian(entryBuffer.AsSpan(writeOffset, 8), offset);
            writeOffset += 8;

            // Write Type (1 byte)
            entryBuffer[writeOffset++] = (byte)qid.Type;

            // Write Name (2 + nameLen bytes)
            BinaryPrimitives.WriteUInt16LittleEndian(entryBuffer.AsSpan(writeOffset, 2), (ushort)nameLen);
            writeOffset += 2;
            Encoding.UTF8.GetBytes(childEntry.Name).CopyTo(entryBuffer.AsSpan(writeOffset, nameLen));

            entries.AddRange(entryBuffer);

            // Stop if we've exceeded the count
            if (entries.Count >= treaddir.Count) break;
        }

        var allData = entries.ToArray();
        var chunk = allData.AsSpan(0, (int)Math.Min((long)treaddir.Count, (long)allData.Length)).ToArray();

        return Task.FromResult(new Rreaddir(
            (uint)(chunk.Length + NinePConstants.HeaderSize + 4),
            treaddir.Tag,
            (uint)chunk.Length,
            chunk));
    }

    public INinePFileSystem Clone()
    {
        var clone = new MockFileSystem(_vault);
        clone._currentPath = new List<string>(_currentPath);
        clone.Dialect = Dialect;

        // Deep copy entries
        foreach (var kvp in _entries)
        {
            clone._entries[kvp.Key] = new MockEntry
            {
                Name = kvp.Value.Name,
                Type = kvp.Value.Type,
                Mode = kvp.Value.Mode,
                Gid = kvp.Value.Gid,
                Qid = kvp.Value.Qid,
                Content = (byte[])kvp.Value.Content.Clone(),
                Created = kvp.Value.Created
            };
        }

        return clone;
    }
}
