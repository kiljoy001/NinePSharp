using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NinePSharp.Constants;
using NinePSharp.Server.Utils;
using NinePSharp.Messages;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Examples;

public class InMemoryHandler : INinePRequestHandler
{
    private class Node
    {
        public string Name { get; set; } = string.Empty;
        public Qid Qid { get; set; }
        public uint Mode { get; set; }
        public uint Atime { get; set; }
        public uint Mtime { get; set; }
        public ulong Length { get; set; }
        public string Uid { get; set; } = "root";
        public string Gid { get; set; } = "root";
        public string Muid { get; set; } = "root";

        public byte[] Content { get; set; } = Array.Empty<byte>();
        public ConcurrentDictionary<string, Node> Children { get; } = new();

        public bool IsDirectory => (Mode & (uint)NinePConstants.FileMode9P.DMDIR) != 0;
    }

    private readonly Node _root;
    private long _nextPath = 1;

    public InMemoryHandler()
    {
        _root = new Node
        {
            Name = "/",
            Qid = new Qid(QidType.QTDIR, 0, (ulong)Interlocked.Increment(ref _nextPath)),
            Mode = (uint)NinePConstants.FileMode9P.DMDIR | 0755,
            Atime = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Mtime = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
    }

    public void AddDirectory(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = _root;
        foreach (var segment in segments)
        {
            current = current.Children.GetOrAdd(segment, name => new Node
            {
                Name = name,
                Qid = new Qid(QidType.QTDIR, 0, (ulong)Interlocked.Increment(ref _nextPath)),
                Mode = (uint)NinePConstants.FileMode9P.DMDIR | 0755,
                Atime = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Mtime = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        }
    }

    public void AddFile(string path, string content)
    {
        AddFile(path, System.Text.Encoding.UTF8.GetBytes(content));
    }

    public void AddFile(string path, byte[] content)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return;

        var current = _root;
        for (int i = 0; i < segments.Length - 1; i++)
        {
            current = current.Children.GetOrAdd(segments[i], name => new Node
            {
                Name = name,
                Qid = new Qid(QidType.QTDIR, 0, (ulong)Interlocked.Increment(ref _nextPath)),
                Mode = (uint)NinePConstants.FileMode9P.DMDIR | 0755,
                Atime = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Mtime = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        }

        var fileName = segments[^1];
        var fileNode = new Node
        {
            Name = fileName,
            Qid = new Qid(QidType.QTFILE, 0, (ulong)Interlocked.Increment(ref _nextPath)),
            Mode = 0644,
            Atime = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Mtime = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Content = content,
            Length = (ulong)content.Length
        };

        current.Children[fileName] = fileNode;
    }

    private Node? GetNode(string[] relativePath)
    {
        var current = _root;
        foreach (var segment in relativePath)
        {
            if (!current.IsDirectory || !current.Children.TryGetValue(segment, out current))
            {
                return null;
            }
        }
        return current;
    }

    public virtual Task<Rwalk> WalkAsync(string[] relativePath, Twalk msg, CancellationToken ct)
    {
        var node = GetNode(relativePath);
        if (node == null)
            return Task.FromException<Rwalk>(new NinePProtocolException("File not found"));

        var qids = new List<Qid>();
        var current = node;

        foreach (var segment in msg.Wname)
        {
            if (!current.IsDirectory || !current.Children.TryGetValue(segment, out var next))
            {
                break;
            }
            current = next;
            qids.Add(current.Qid);
        }

        return Task.FromResult(new Rwalk(msg.Tag, qids.ToArray()));
    }

    public virtual Task<Ropen> OpenAsync(string[] relativePath, Topen msg, CancellationToken ct)
    {
        var node = GetNode(relativePath);
        if (node == null)
            return Task.FromException<Ropen>(new NinePProtocolException("File not found"));

        return Task.FromResult(new Ropen(msg.Tag, node.Qid, 4096)); // Arbitrary iounit
    }

    public virtual Task<Rread> ReadAsync(string[] relativePath, Tread msg, CancellationToken ct)
    {
        var node = GetNode(relativePath);
        if (node == null)
            return Task.FromException<Rread>(new NinePProtocolException("File not found"));

        if (node.IsDirectory)
        {
            // Directory read - return error and let fall back to Treaddir if needed,
            // or we could implement P9 directory reading here. For simplicity, we just
            // implement pure ReaddirAsync and throw here.
            return Task.FromException<Rread>(new NinePProtocolException("Is a directory"));
        }

        ulong offset = msg.Offset;
        uint count = msg.Count;

        if (offset >= (ulong)node.Content.Length)
        {
            return Task.FromResult(new Rread(msg.Tag, Array.Empty<byte>()));
        }

        var available = (uint)(node.Content.Length - (int)offset);
        var toRead = Math.Min(count, available);

        var data = new byte[toRead];
        Array.Copy(node.Content, (int)offset, data, 0, (int)toRead);

        return Task.FromResult(new Rread(msg.Tag, data));
    }

    public virtual Task<Rwrite> WriteAsync(string[] relativePath, Twrite msg, CancellationToken ct)
    {
        var node = GetNode(relativePath);
        if (node == null)
            return Task.FromException<Rwrite>(new NinePProtocolException("File not found"));

        if (node.IsDirectory)
            return Task.FromException<Rwrite>(new NinePProtocolException("Is a directory"));

        var newLength = Math.Max((int)node.Length, (int)msg.Offset + msg.Data.Length);
        var newBuffer = new byte[newLength];

        Array.Copy(node.Content, newBuffer, node.Content.Length);
        var msgDataArray = msg.Data.ToArray();
        Array.Copy(msgDataArray, 0, newBuffer, (int)msg.Offset, msgDataArray.Length);

        node.Content = newBuffer;
        node.Length = (ulong)newLength;
        node.Mtime = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        node.Qid = new Qid(node.Qid.Type, node.Qid.Version + 1, node.Qid.Path);

        return Task.FromResult(new Rwrite(msg.Tag, (uint)msg.Data.Length));
    }

    public virtual Task<Rstat> StatAsync(string[] relativePath, Tstat msg, CancellationToken ct)
    {
        var node = GetNode(relativePath);
        if (node == null)
            return Task.FromException<Rstat>(new NinePProtocolException("File not found"));

        var stat = new Stat(
            size: 0,
            type: 0,
            dev: 0,
            qid: node.Qid,
            mode: node.Mode,
            atime: node.Atime,
            mtime: node.Mtime,
            length: node.Length,
            name: node.Name,
            uid: node.Uid,
            gid: node.Gid,
            muid: node.Muid
        );

        return Task.FromResult(new Rstat(msg.Tag, stat));
    }

    public virtual Task<Rwstat> WstatAsync(string[] relativePath, Twstat msg, CancellationToken ct)
    {
        var node = GetNode(relativePath);
        if (node == null)
            return Task.FromException<Rwstat>(new NinePProtocolException("File not found"));

        if (msg.Stat.Name != null && msg.Stat.Name.Length > 0)
        {
            // Rename logic - requires parent node
            if (relativePath.Length > 0)
            {
                var parentPath = relativePath.Take(relativePath.Length - 1).ToArray();
                var parent = GetNode(parentPath);
                if (parent != null)
                {
                    parent.Children.TryRemove(node.Name, out _);
                    node.Name = msg.Stat.Name;
                    parent.Children.TryAdd(node.Name, node);
                }
            }
            else
            {
                return Task.FromException<Rwstat>(new NinePProtocolException("Cannot rename root"));
            }
        }

        if (msg.Stat.Mode != uint.MaxValue) node.Mode = msg.Stat.Mode;
        if (msg.Stat.Mtime != uint.MaxValue) node.Mtime = msg.Stat.Mtime;
        if (msg.Stat.Length != ulong.MaxValue)
        {
            node.Length = msg.Stat.Length;
            var newContent = new byte[node.Length];
            Array.Copy(node.Content, newContent, Math.Min((int)node.Length, node.Content.Length));
            node.Content = newContent;
        }

        return Task.FromResult(new Rwstat(msg.Tag));
    }

    public virtual Task<Rcreate> CreateAsync(string[] parentPath, Tcreate msg, CancellationToken ct)
    {
        var parent = GetNode(parentPath);
        if (parent == null)
            return Task.FromException<Rcreate>(new NinePProtocolException("Parent not found"));

        if (!parent.IsDirectory)
            return Task.FromException<Rcreate>(new NinePProtocolException("Parent is not a directory"));

        var isDir = (msg.Perm & (uint)NinePConstants.FileMode9P.DMDIR) != 0;
        var qidType = isDir ? QidType.QTDIR : QidType.QTFILE;
        var qid = new Qid(qidType, 0, (ulong)Interlocked.Increment(ref _nextPath));

        var newNode = new Node
        {
            Name = msg.Name,
            Qid = qid,
            Mode = msg.Perm,
            Atime = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Mtime = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        if (!parent.Children.TryAdd(msg.Name, newNode))
            return Task.FromException<Rcreate>(new NinePProtocolException("File already exists"));

        return Task.FromResult(new Rcreate(msg.Tag, qid, 4096));
    }

    public virtual Task<Rremove> RemoveAsync(string[] relativePath, Tremove msg, CancellationToken ct)
    {
        if (relativePath.Length == 0)
            return Task.FromException<Rremove>(new NinePProtocolException("Cannot remove root"));

        var parentPath = relativePath.Take(relativePath.Length - 1).ToArray();
        var parent = GetNode(parentPath);
        if (parent == null)
            return Task.FromException<Rremove>(new NinePProtocolException("Parent not found"));

        var name = relativePath.Last();
        if (!parent.Children.TryRemove(name, out _))
            return Task.FromException<Rremove>(new NinePProtocolException("File not found"));

        return Task.FromResult(new Rremove(msg.Tag));
    }

    public virtual Task<Rreaddir> ReaddirAsync(string[] relativePath, Treaddir msg, CancellationToken ct)
    {
        var node = GetNode(relativePath);
        if (node == null || !node.IsDirectory)
            return Task.FromException<Rreaddir>(new NinePProtocolException("Not a directory"));

        var allStats = new List<byte>();
        foreach (var child in node.Children.Values)
        {
            var stat = new Stat(0, 0, 0, child.Qid, child.Mode, child.Atime, child.Mtime, child.Length, child.Name, child.Uid, child.Gid, child.Muid);
            var buffer = new byte[stat.Size];
            int off = 0;
            stat.WriteTo(buffer, ref off);
            allStats.AddRange(buffer);
        }

        if (msg.Offset >= (ulong)allStats.Count)
            return Task.FromResult(new Rreaddir((uint)(NinePConstants.HeaderSize + 4), msg.Tag, 0, Array.Empty<byte>()));

        int start = (int)msg.Offset;
        int len = (int)Math.Min(msg.Count, (uint)(allStats.Count - start));
        var data = allStats.GetRange(start, len).ToArray();
        return Task.FromResult(new Rreaddir((uint)(NinePConstants.HeaderSize + 4 + data.Length), msg.Tag, (uint)data.Length, data));
    }

    /// <summary>
    /// Auth read stub - InMemoryHandler does not require authentication.
    /// Returns empty data signaling "no auth required".
    /// </summary>
    public virtual Task<byte[]> AuthReadAsync(uint afid, ulong offset, uint count, CancellationToken ct)
    {
        return Task.FromResult(Array.Empty<byte>());
    }

    /// <summary>
    /// Auth write stub - accepts any auth data silently.
    /// </summary>
    public virtual Task<uint> AuthWriteAsync(uint afid, ulong offset, byte[] data, CancellationToken ct)
    {
        return Task.FromResult((uint)data.Length);
    }
}
