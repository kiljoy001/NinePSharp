using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Server.Interfaces;
using NinePSharp.Protocol;

namespace NinePSharp.Server.FileSystem;

public class FileSystemBackend : IBackendRuntime, IReaddirCapableBackendRuntime, INinePRequestHandler
{
    private readonly NinePDir _root;
    private readonly ConcurrentDictionary<ushort, CancellationTokenSource> _inFlightRequests = new();

    public string Id { get; } = Guid.NewGuid().ToString();
    public string MountPath { get; }
    public NinePDialect Dialect { get; set; }

    public FileSystemBackend(NinePDir root, string mountPath = "")
    {
        _root = root;
        MountPath = mountPath;
    }

    private async Task<T> WithCancellation<T>(ushort tag, CancellationToken ct, Func<CancellationToken, Task<T>> action)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (!_inFlightRequests.TryAdd(tag, cts))
        {
             // Tag collision should be handled by protocol layer, but we'll protect here too
             _inFlightRequests[tag].Cancel();
             _inFlightRequests[tag] = cts;
        }

        try
        {
            return await action(cts.Token);
        }
        finally
        {
            if (_inFlightRequests.TryGetValue(tag, out var currentCts) && currentCts == cts)
            {
                _inFlightRequests.TryRemove(tag, out _);
            }
            cts.Dispose();
        }
    }

    public Task<IAuthHandler?> GetAuthHandlerAsync(Tauth msg, CancellationToken ct) => Task.FromResult<IAuthHandler?>(null);

    public async Task<Rattach> AttachAsync(Tattach msg, CancellationToken ct)
    {
        return await WithCancellation(msg.Tag, ct, _ => Task.FromResult(new Rattach(msg.Tag, _root.GetStat(Dialect).Qid)));
    }

    private async Task<INinePNode> ResolveNode(string[] relativePath, CancellationToken ct)
    {
        INinePNode current = _root;
        foreach (var segment in relativePath)
        {
            if (string.IsNullOrEmpty(segment) || segment == ".") continue;
            var next = await current.WalkAsync(segment, ct);
            if (next == null) throw new Exception($"Path not found: {segment}");
            current = next;
        }
        return current;
    }

    public async Task<Rwalk> WalkAsync(string[] relativePath, Twalk msg, CancellationToken ct)
    {
        return await WithCancellation(msg.Tag, ct, async (token) => {
            var wqids = new List<Qid>();
            INinePNode current = await ResolveNode(relativePath, token);
            foreach (var segment in msg.Wname)
            {
                var next = await current.WalkAsync(segment, token);
                if (next == null) break;
                wqids.Add(next.GetStat(Dialect).Qid);
                current = next;
            }
            return new Rwalk(msg.Tag, wqids.ToArray());
        });
    }

    public async Task<Rwalk> WalkAsync(string[] relativePath, NinePDialect dialect) 
        => await WalkAsync(relativePath, new Twalk(0, 0, 0, Array.Empty<string>()), CancellationToken.None);

    public async Task<Ropen> OpenAsync(string[] relativePath, Topen msg, CancellationToken ct)
    {
        return await WithCancellation(msg.Tag, ct, async (token) => {
            var node = await ResolveNode(relativePath, token);
            return new Ropen(msg.Tag, node.GetStat(Dialect).Qid, 0);
        });
    }

    public async Task<Ropen> OpenAsync(string[] relativePath, Topen topen, NinePDialect dialect, CancellationToken ct = default)
        => await OpenAsync(relativePath, topen, ct);

    public async Task<Rread> ReadAsync(string[] relativePath, Tread msg, CancellationToken ct)
    {
        return await WithCancellation(msg.Tag, ct, async (token) => {
            var node = await ResolveNode(relativePath, token);
            var data = await node.ReadAsync(msg.Offset, msg.Count, token);
            return new Rread(msg.Tag, data);
        });
    }

    public async Task<Rread> ReadAsync(string[] relativePath, Tread tread, NinePDialect dialect, CancellationToken ct = default)
        => await ReadAsync(relativePath, tread, ct);

    public async Task<Rwrite> WriteAsync(string[] relativePath, Twrite msg, CancellationToken ct)
    {
        return await WithCancellation(msg.Tag, ct, async (token) => {
            var node = await ResolveNode(relativePath, token);
            var count = await node.WriteAsync(msg.Offset, msg.Data.ToArray(), token);
            return new Rwrite(msg.Tag, count);
        });
    }

    public async Task<Rwrite> WriteAsync(string[] relativePath, Twrite twrite, NinePDialect dialect, CancellationToken ct = default)
        => await WriteAsync(relativePath, twrite, ct);

    public async Task<Rclunk> ClunkAsync(string[] relativePath, Tclunk msg, CancellationToken ct)
    {
        return await WithCancellation(msg.Tag, ct, _ => Task.FromResult(new Rclunk(msg.Tag)));
    }

    public Task<Rclunk> ClunkAsync(string[] relativePath, Tclunk tclunk, NinePDialect dialect)
        => ClunkAsync(relativePath, tclunk, CancellationToken.None);

    public async Task<Rstat> StatAsync(string[] relativePath, Tstat msg, CancellationToken ct)
    {
        return await WithCancellation(msg.Tag, ct, async (token) => {
            var node = await ResolveNode(relativePath, token);
            return new Rstat(msg.Tag, node.GetStat(Dialect));
        });
    }

    public async Task<Rstat> StatAsync(string[] relativePath, Tstat tstat, NinePDialect dialect, CancellationToken ct = default)
        => await StatAsync(relativePath, tstat, ct);

    public async Task<Rwstat> WstatAsync(string[] relativePath, Twstat msg, CancellationToken ct)
    {
        return await WithCancellation(msg.Tag, ct, async (token) => {
            var node = await ResolveNode(relativePath, token);
            await node.WstatAsync(msg.Stat, token);
            return new Rwstat(msg.Tag);
        });
    }

    public async Task<Rwstat> WstatAsync(string[] relativePath, Twstat twstat, NinePDialect dialect, CancellationToken ct = default)
        => await WstatAsync(relativePath, twstat, ct);

    public async Task<Rremove> RemoveAsync(string[] relativePath, Tremove msg, CancellationToken ct)
    {
        return await WithCancellation(msg.Tag, ct, async (token) => {
            if (relativePath.Length == 0) throw new Exception("Cannot remove root.");
            var parentPath = relativePath.Take(relativePath.Length - 1).ToArray();
            var name = relativePath.Last();
            var parent = await ResolveNode(parentPath, token);
            await parent.RemoveAsync(name, token);
            return new Rremove(msg.Tag);
        });
    }

    public async Task<Rremove> RemoveAsync(string[] relativePath, Tremove tremove, NinePDialect dialect, CancellationToken ct = default)
        => await RemoveAsync(relativePath, tremove, ct);

    public async Task<Rcreate> CreateAsync(string[] parentPath, Tcreate msg, CancellationToken ct)
    {
        return await WithCancellation(msg.Tag, ct, async (token) => {
            var parent = await ResolveNode(parentPath, token);
            var newNode = await parent.CreateAsync(msg.Name, msg.Perm, msg.Mode, token);
            return new Rcreate(msg.Tag, newNode.GetStat(Dialect).Qid, 0);
        });
    }

    public async Task<Rcreate> CreateAsync(string[] parentRelativePath, Tcreate tcreate, NinePDialect dialect, CancellationToken ct = default)
        => await CreateAsync(parentRelativePath, tcreate, ct);

    public async Task<Rsymlink> SymlinkAsync(string[] relativePath, Tsymlink msg, CancellationToken ct)
    {
        return await WithCancellation(msg.Tag, ct, async (token) => {
            var parent = await ResolveNode(relativePath, token);
            await parent.SymlinkAsync(msg.Name, msg.Symtgt, token);
            var linkNode = await parent.WalkAsync(msg.Name, token);
            uint size = (uint)(NinePConstants.HeaderSize + 13);
            return new Rsymlink(size, msg.Tag, linkNode!.GetStat(Dialect).Qid);
        });
    }

    public async Task<Rreadlink> ReadlinkAsync(string[] relativePath, Treadlink msg, CancellationToken ct)
    {
        return await WithCancellation(msg.Tag, ct, async (token) => {
            var node = await ResolveNode(relativePath, token);
            var target = await node.ReadlinkAsync(token);
            uint size = (uint)(NinePConstants.HeaderSize + 2 + System.Text.Encoding.UTF8.GetByteCount(target));
            return new Rreadlink(size, msg.Tag, target);
        });
    }

    public Task<Rlink> LinkAsync(string[] relativePath, Tlink msg, CancellationToken ct)
    {
        throw new NotSupportedException("LinkAsync requires node resolution for oldfid which is not supported in this simplified backend.");
    }

    public async Task<Rlerror> LockAsync(string[] relativePath, Tlock msg, CancellationToken ct)
    {
        return await WithCancellation(msg.Tag, ct, async (token) => {
            var node = await ResolveNode(relativePath, token);
            return await node.LockAsync(msg, token);
        });
    }

    public async Task<Rgetlock> GetlockAsync(string[] relativePath, Tgetlock msg, CancellationToken ct)
    {
        return await WithCancellation(msg.Tag, ct, async (token) => {
            var node = await ResolveNode(relativePath, token);
            return await node.GetlockAsync(msg, token);
        });
    }

    public async Task<Rxattrwalk> XattrwalkAsync(string[] relativePath, Txattrwalk msg, CancellationToken ct)
    {
        return await WithCancellation(msg.Tag, ct, async (token) => {
            var node = await ResolveNode(relativePath, token);
            return await node.XattrwalkAsync(msg, token);
        });
    }

    public async Task<Rxattrcreate> XattrcreateAsync(string[] relativePath, Txattrcreate msg, CancellationToken ct)
    {
        return await WithCancellation(msg.Tag, ct, async (token) => {
            var node = await ResolveNode(relativePath, token);
            return await node.XattrcreateAsync(msg, token);
        });
    }

    public Task<Rflush> FlushAsync(Tflush msg, CancellationToken ct)
    {
        if (_inFlightRequests.TryGetValue(msg.OldTag, out var cts))
        {
            cts.Cancel();
        }
        return Task.FromResult(new Rflush(msg.Tag));
    }

    public async Task<Rreaddir> ReaddirAsync(string[] relativePath, Treaddir msg, CancellationToken ct)
    {
        return await WithCancellation(msg.Tag, ct, async (token) => {
            var node = await ResolveNode(relativePath, token);
            var entries = await node.ReaddirAsync(token);
            
            var fullBuffer = new List<byte>();
            ulong currentOffset = 0;
            foreach (var entry in entries)
            {
                var stat = entry.GetStat(Dialect);
                var nameBytes = System.Text.Encoding.UTF8.GetBytes(stat.Name);
                var entryBuffer = new byte[13 + 8 + 1 + 2 + nameBytes.Length];
                int offset = 0;
                var span = entryBuffer.AsSpan();
                span.WriteQid(stat.Qid, ref offset);
                currentOffset += (ulong)entryBuffer.Length;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), currentOffset);
                offset += 8;
                span[offset++] = (byte)(stat.Qid.Type);
                span.WriteString(stat.Name, ref offset);
                fullBuffer.AddRange(entryBuffer);
            }

            var resultData = fullBuffer.Skip((int)msg.Offset).Take((int)msg.Count).ToArray();
            uint totalSize = (uint)(NinePConstants.HeaderSize + 4 + resultData.Length);
            return new Rreaddir(totalSize, msg.Tag, (uint)resultData.Length, resultData);
        });
    }

    public Task<Rreaddir> ReaddirAsync(string[] relativePath, Treaddir treaddir, NinePDialect dialect, CancellationToken ct = default)
        => ReaddirAsync(relativePath, treaddir, ct);

    public Task<Rreaddir> ReaddirCompatAsync(string[] relativePath, Treaddir treaddir, NinePDialect dialect, CancellationToken ct = default)
        => ReaddirAsync(relativePath, treaddir, ct);
}

public class FileSystemProtocolBackend : IProtocolBackend
{
    private readonly NinePDir _root;
    public string Name => "FileSystem";
    public string MountPath { get; private set; }

    public FileSystemProtocolBackend(NinePDir root, string mountPath = "/")
    {
        _root = root;
        MountPath = mountPath;
    }

    public Task InitializeAsync(IConfiguration configuration)
    {
        MountPath = configuration["MountPath"] ?? MountPath;
        return Task.CompletedTask;
    }

    public INinePFileSystem GetFileSystem(X509Certificate2? certificate = null)
    {
        return new FileSystemWrapper(new FileSystemBackend(_root, MountPath));
    }

    public INinePFileSystem GetFileSystem(SecureString? credentials, X509Certificate2? certificate = null)
    {
        return GetFileSystem(certificate);
    }
}

internal class FileSystemWrapper : INinePFileSystem, IReaddirCapableBackendRuntime
{
    private readonly FileSystemBackend _inner;
    public FileSystemWrapper(FileSystemBackend inner) => _inner = inner;

    public string Id => _inner.Id;
    public string MountPath => _inner.MountPath;
    public NinePDialect Dialect { get => _inner.Dialect; set => _inner.Dialect = value; }

    public Task<Rwalk> WalkAsync(string[] relativePath, NinePDialect dialect) => _inner.WalkAsync(relativePath, dialect);
    public Task<Ropen> OpenAsync(string[] relativePath, Topen topen, NinePDialect dialect, CancellationToken ct = default) => _inner.OpenAsync(relativePath, topen, dialect, ct);
    public Task<Rread> ReadAsync(string[] relativePath, Tread tread, NinePDialect dialect, CancellationToken ct = default) => _inner.ReadAsync(relativePath, tread, dialect, ct);
    public Task<Rwrite> WriteAsync(string[] relativePath, Twrite twrite, NinePDialect dialect, CancellationToken ct = default) => _inner.WriteAsync(relativePath, twrite, dialect, ct);
    public Task<Rclunk> ClunkAsync(string[] relativePath, Tclunk tclunk, NinePDialect dialect) => _inner.ClunkAsync(relativePath, tclunk, dialect);
    public Task<Rstat> StatAsync(string[] relativePath, Tstat tstat, NinePDialect dialect, CancellationToken ct = default) => _inner.StatAsync(relativePath, tstat, dialect, ct);
    public Task<Rwstat> WstatAsync(string[] relativePath, Twstat twstat, NinePDialect dialect, CancellationToken ct = default) => _inner.WstatAsync(relativePath, twstat, dialect, ct);
    public Task<Rremove> RemoveAsync(string[] relativePath, Tremove tremove, NinePDialect dialect, CancellationToken ct = default) => _inner.RemoveAsync(relativePath, tremove, dialect, ct);
    public Task<Rcreate> CreateAsync(string[] parentRelativePath, Tcreate tcreate, NinePDialect dialect, CancellationToken ct = default) => _inner.CreateAsync(parentRelativePath, tcreate, dialect, ct);
    public Task<Rreaddir> ReaddirAsync(string[] relativePath, Treaddir treaddir, NinePDialect dialect, CancellationToken ct = default) => _inner.ReaddirAsync(relativePath, treaddir, dialect, ct);
    public Task<Rreaddir> ReaddirCompatAsync(string[] relativePath, Treaddir treaddir, NinePDialect dialect, CancellationToken ct = default) => _inner.ReaddirCompatAsync(relativePath, treaddir, dialect, ct);

    public Task<Rsymlink> SymlinkAsync(string[] relativePath, Tsymlink msg, CancellationToken ct) => _inner.SymlinkAsync(relativePath, msg, ct);
    public Task<Rreadlink> ReadlinkAsync(string[] relativePath, Treadlink msg, CancellationToken ct) => _inner.ReadlinkAsync(relativePath, msg, ct);
    public Task<Rlink> LinkAsync(string[] relativePath, Tlink msg, CancellationToken ct) => _inner.LinkAsync(relativePath, msg, ct);
    public Task<Rlerror> LockAsync(string[] relativePath, Tlock msg, CancellationToken ct) => _inner.LockAsync(relativePath, msg, ct);
    public Task<Rgetlock> GetlockAsync(string[] relativePath, Tgetlock msg, CancellationToken ct) => _inner.GetlockAsync(relativePath, msg, ct);
    public Task<Rxattrwalk> XattrwalkAsync(string[] relativePath, Txattrwalk msg, CancellationToken ct) => _inner.XattrwalkAsync(relativePath, msg, ct);
    public Task<Rxattrcreate> XattrcreateAsync(string[] relativePath, Txattrcreate msg, CancellationToken ct) => _inner.XattrcreateAsync(relativePath, msg, ct);
    public Task<Rflush> FlushAsync(Tflush msg, CancellationToken ct) => _inner.FlushAsync(msg, ct);
}
