using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Constants;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Backends;

/// <summary>
/// 9front-style stateless mock filesystem backend.
/// No mutable cursor state - path passed to each operation.
/// Implements IBackendRuntime directly (the 9front devtab model).
/// </summary>
public class MockFileSystem : IBackendRuntime, IReaddirCapableBackendRuntime
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

    public string Id { get; }
    public string MountPath { get; }
    public NinePDialect Dialect { get; set; } = NinePDialect.NineP2000;

    public MockFileSystem(ILuxVaultService vault, string id = "mock", string mountPath = "/mock")
    {
        _vault = vault;
        Id = id;
        MountPath = mountPath;
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

    private static string PathFromSegments(string[] segments)
    {
        if (segments == null || segments.Length == 0) return "/";
        return "/" + string.Join("/", segments);
    }

    public Task<Rwalk> WalkAsync(string[] relativePath, NinePDialect dialect)
    {
        var qids = new List<Qid>();
        var currentSegments = new List<string>();

        foreach (var name in relativePath)
        {
            if (name == "..")
            {
                if (currentSegments.Count > 0)
                    currentSegments.RemoveAt(currentSegments.Count - 1);
            }
            else if (name != "." && name != "")
            {
                currentSegments.Add(name);
            }

            var checkPath = PathFromSegments(currentSegments.ToArray());
            if (_entries.TryGetValue(checkPath, out var entry))
            {
                var qidType = entry.Type == MockEntryType.Directory ? QidType.QTDIR : QidType.QTFILE;
                qids.Add(new Qid(qidType, 0, entry.Qid));
            }
            else
            {
                // Path doesn't exist - return partial walk
                break;
            }
        }

        return Task.FromResult(new Rwalk(0, qids.ToArray()));
    }

    public Task<Ropen> OpenAsync(string[] relativePath, Topen topen, NinePDialect dialect)
    {
        var path = PathFromSegments(relativePath);
        if (_entries.TryGetValue(path, out var entry))
        {
            var qidType = entry.Type == MockEntryType.Directory ? QidType.QTDIR : QidType.QTFILE;
            return Task.FromResult(new Ropen(topen.Tag, new Qid(qidType, 0, entry.Qid), 8192));
        }
        return Task.FromResult(new Ropen(topen.Tag, new Qid(QidType.QTFILE, 0, 0), 8192));
    }

    public Task<Rread> ReadAsync(string[] relativePath, Tread tread, NinePDialect dialect, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var path = PathFromSegments(relativePath);

        if (_entries.TryGetValue(path, out var entry))
        {
            if (entry.Type == MockEntryType.File)
            {
                if (tread.Offset >= (ulong)entry.Content.Length)
                    return Task.FromResult(new Rread(tread.Tag, Array.Empty<byte>()));

                var offset = (int)tread.Offset;
                var count = (int)Math.Min(tread.Count, (uint)(entry.Content.Length - offset));
                var data = new byte[count];
                Array.Copy(entry.Content, offset, data, 0, count);
                return Task.FromResult(new Rread(tread.Tag, data));
            }
            else if (entry.Type == MockEntryType.Directory)
            {
                var children = _entries
                    .Where(e => {
                        var parentPath = Path.GetDirectoryName(e.Key)?.Replace('\\', '/') ?? "/";
                        return parentPath == path && e.Key != path;
                    })
                    .OrderBy(e => e.Key)
                    .ToList();

                var allStats = new List<byte>();
                foreach (var child in children)
                {
                    var childEntry = child.Value;
                    var qidType = childEntry.Type == MockEntryType.Directory ? QidType.QTDIR : QidType.QTFILE;
                    var mode = childEntry.Mode;
                    if (childEntry.Type == MockEntryType.Directory)
                        mode |= (uint)NinePConstants.FileMode9P.DMDIR;

                    var stat = new Stat(0, 0, 0, new Qid(qidType, 0, childEntry.Qid), mode, 0, 0,
                        (ulong)childEntry.Content.Length, childEntry.Name, "none", "none", "none", dialect: dialect);
                    var buffer = new byte[stat.Size];
                    int off = 0;
                    stat.WriteTo(buffer, ref off);
                    allStats.AddRange(buffer);
                }

                if (tread.Offset >= (ulong)allStats.Count)
                    return Task.FromResult(new Rread(tread.Tag, Array.Empty<byte>()));

                int start = (int)tread.Offset;
                int len = (int)Math.Min(tread.Count, (uint)(allStats.Count - start));
                return Task.FromResult(new Rread(tread.Tag, allStats.GetRange(start, len).ToArray()));
            }
        }

        return Task.FromResult(new Rread(tread.Tag, Array.Empty<byte>()));
    }

    public Task<Rwrite> WriteAsync(string[] relativePath, Twrite twrite, NinePDialect dialect, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var path = PathFromSegments(relativePath);

        if (_entries.TryGetValue(path, out var entry) && entry.Type == MockEntryType.File)
        {
            var offset = (int)twrite.Offset;
            var data = twrite.Data.ToArray();

            var newLength = Math.Max(entry.Content.Length, offset + data.Length);
            var newContent = new byte[newLength];
            Array.Copy(entry.Content, newContent, entry.Content.Length);
            Array.Copy(data, 0, newContent, offset, data.Length);
            entry.Content = newContent;

            return Task.FromResult(new Rwrite(twrite.Tag, (uint)data.Length));
        }

        return Task.FromResult(new Rwrite(twrite.Tag, (uint)twrite.Data.Length));
    }

    public Task<Rclunk> ClunkAsync(string[] relativePath, Tclunk tclunk, NinePDialect dialect)
    {
        // Stateless - nothing to clean up
        return Task.FromResult(new Rclunk(tclunk.Tag));
    }

    public Task<Rstat> StatAsync(string[] relativePath, Tstat tstat, NinePDialect dialect)
    {
        var path = PathFromSegments(relativePath);

        if (_entries.TryGetValue(path, out var entry))
        {
            var qidType = entry.Type == MockEntryType.Directory ? QidType.QTDIR : QidType.QTFILE;
            var mode = entry.Mode;
            if (entry.Type == MockEntryType.Directory)
                mode |= (uint)NinePConstants.FileMode9P.DMDIR;

            var stat = new Stat(0, 0, 0, new Qid(qidType, 0, entry.Qid), mode, 0, 0,
                (ulong)entry.Content.Length, entry.Name, "none", "none", "none", dialect: dialect);
            return Task.FromResult(new Rstat(tstat.Tag, stat));
        }
        else
        {
            var name = relativePath.Length > 0 ? relativePath[^1] : "mock";
            var stat = new Stat(0, 0, 0, new Qid(QidType.QTFILE, 0, 1), 0644, 0, 0, 0, name, "none", "none", "none", dialect: dialect);
            return Task.FromResult(new Rstat(tstat.Tag, stat));
        }
    }

    public Task<Rwstat> WstatAsync(string[] relativePath, Twstat twstat, NinePDialect dialect)
    {
        var path = PathFromSegments(relativePath);
        if (_entries.TryGetValue(path, out var entry))
        {
            if (twstat.Stat.Mode != 0xFFFFFFFF)
                entry.Mode = twstat.Stat.Mode;
            return Task.FromResult(new Rwstat(twstat.Tag));
        }
        throw new NinePProtocolException("File not found");
    }

    public Task<Rremove> RemoveAsync(string[] relativePath, Tremove tremove, NinePDialect dialect)
    {
        var path = PathFromSegments(relativePath);
        if (path == "/") throw new NinePProtocolException("Cannot remove root");

        if (_entries.TryRemove(path, out _))
            return Task.FromResult(new Rremove(tremove.Tag));

        throw new NinePProtocolException("File not found");
    }

    public Task<Rcreate> CreateAsync(string[] parentRelativePath, Tcreate tcreate, NinePDialect dialect)
    {
        var parentPath = PathFromSegments(parentRelativePath);
        var newPath = parentPath == "/" ? "/" + tcreate.Name : parentPath + "/" + tcreate.Name;

        if (!_entries.TryGetValue(parentPath, out var parent) || parent.Type != MockEntryType.Directory)
            throw new NinePProtocolException("Parent directory does not exist");

        var isDir = (tcreate.Perm & (uint)NinePConstants.FileMode9P.DMDIR) != 0;
        var qid = (ulong)(newPath.GetHashCode() & 0x7FFFFFFF);
        var entry = new MockEntry
        {
            Name = tcreate.Name,
            Type = isDir ? MockEntryType.Directory : MockEntryType.File,
            Mode = tcreate.Perm,
            Qid = qid
        };

        _entries[newPath] = entry;
        var qidType = isDir ? QidType.QTDIR : QidType.QTFILE;
        return Task.FromResult(new Rcreate(tcreate.Tag, new Qid(qidType, 0, qid), 8192));
    }

    public Task<Rreaddir> ReaddirAsync(string[] relativePath, Treaddir treaddir, NinePDialect dialect, CancellationToken ct = default)
    {
        var path = PathFromSegments(relativePath);

        if (!_entries.TryGetValue(path, out var entry) || entry.Type != MockEntryType.Directory)
            return Task.FromResult(new Rreaddir((uint)(NinePConstants.HeaderSize + 4), treaddir.Tag, 0, Array.Empty<byte>()));

        var children = _entries
            .Where(e => {
                var parentPath = Path.GetDirectoryName(e.Key)?.Replace('\\', '/') ?? "/";
                return parentPath == path && e.Key != path;
            })
            .OrderBy(e => e.Key)
            .ToList();

        var allStats = new List<byte>();
        foreach (var child in children)
        {
            var childEntry = child.Value;
            var qidType = childEntry.Type == MockEntryType.Directory ? QidType.QTDIR : QidType.QTFILE;
            var mode = childEntry.Mode;
            if (childEntry.Type == MockEntryType.Directory)
                mode |= (uint)NinePConstants.FileMode9P.DMDIR;

            var stat = new Stat(0, 0, 0, new Qid(qidType, 0, childEntry.Qid), mode, 0, 0,
                (ulong)childEntry.Content.Length, childEntry.Name, "none", "none", "none", dialect: dialect);
            var buffer = new byte[stat.Size];
            int off = 0;
            stat.WriteTo(buffer, ref off);
            allStats.AddRange(buffer);
        }

        if (treaddir.Offset >= (ulong)allStats.Count)
            return Task.FromResult(new Rreaddir((uint)(NinePConstants.HeaderSize + 4), treaddir.Tag, 0, Array.Empty<byte>()));

        int start = (int)treaddir.Offset;
        int len = (int)Math.Min(treaddir.Count, (uint)(allStats.Count - start));
        var data = allStats.GetRange(start, len).ToArray();
        return Task.FromResult(new Rreaddir((uint)(NinePConstants.HeaderSize + 4 + data.Length), treaddir.Tag, (uint)data.Length, data));
    }

    // Test helper methods
    public void AddFile(string path, byte[] content)
    {
        var name = Path.GetFileName(path);
        var qid = (ulong)(path.GetHashCode() & 0x7FFFFFFF);
        _entries[path] = new MockEntry
        {
            Name = name,
            Type = MockEntryType.File,
            Mode = 0644,
            Qid = qid,
            Content = content
        };
    }

    public void AddDirectory(string path)
    {
        var name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(name)) name = "/";
        var qid = (ulong)(path.GetHashCode() & 0x7FFFFFFF);
        _entries[path] = new MockEntry
        {
            Name = name,
            Type = MockEntryType.Directory,
            Mode = 0755,
            Qid = qid
        };
    }
}
