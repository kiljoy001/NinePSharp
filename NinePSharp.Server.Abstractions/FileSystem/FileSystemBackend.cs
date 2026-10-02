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
using NinePSharp.Protocol;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Server.FileSystem;

public class FileSystemBackend : IBackendRuntime, IReaddirCapableBackendRuntime, INinePRequestHandler
{
    private readonly NinePDir root;
    private readonly ConcurrentDictionary<ushort, CancellationTokenSource> inFlightRequests = new();

    public FileSystemBackend(NinePDir root, string mountPath = "")
    {
        this.root = root;
        MountPath = mountPath;
    }

    public string Id { get; } = Guid.NewGuid().ToString();

    public string MountPath { get; }

    public NinePDialect Dialect { get; set; }

    public Task<IAuthHandler?> GetAuthHandlerAsync(Tauth msg, CancellationToken ct) => Task.FromResult<IAuthHandler?>(null);

    public async Task<Rattach> AttachAsync(Tattach msg, CancellationToken ct)
    {
        return await WithCancellation(msg.Tag, ct, _ => Task.FromResult(new Rattach(msg.Tag, root.GetStat(Dialect).Qid)));
    }

    public async Task<Rwalk> WalkAsync(string[] relativePath, Twalk msg, CancellationToken ct)
        => await WalkAsync(relativePath, msg, Dialect, ct);

    public async Task<Rwalk> WalkAsync(string[] relativePath, NinePDialect dialect)
        => await WalkAsync(relativePath, new Twalk(0, 0, 0, Array.Empty<string>()), dialect, CancellationToken.None);

    public async Task<Ropen> OpenAsync(string[] relativePath, Topen msg, CancellationToken ct)
        => await OpenCoreAsync(relativePath, msg, Dialect, ct);

    public async Task<Ropen> OpenAsync(string[] relativePath, Topen topen, NinePDialect dialect, CancellationToken ct = default)
        => await OpenCoreAsync(relativePath, topen, dialect, ct);

    public async Task<Rread> ReadAsync(string[] relativePath, Tread msg, CancellationToken ct)
    {
        return await WithCancellation(msg.Tag, ct, async (token) =>
        {
            var node = await ResolveNode(relativePath, token);
            var data = await node.ReadAsync(msg.Offset, msg.Count, token);
            return new Rread(msg.Tag, data);
        });
    }

    public async Task<Rread> ReadAsync(string[] relativePath, Tread tread, NinePDialect dialect, CancellationToken ct = default)
        => await ReadAsync(relativePath, tread, ct);

    public async Task<Rwrite> WriteAsync(string[] relativePath, Twrite msg, CancellationToken ct)
    {
        return await WithCancellation(msg.Tag, ct, async (token) =>
        {
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
        => await StatCoreAsync(relativePath, msg, Dialect, ct);

    public async Task<Rstat> StatAsync(string[] relativePath, Tstat tstat, NinePDialect dialect, CancellationToken ct = default)
        => await StatCoreAsync(relativePath, tstat, dialect, ct);

    public async Task<Rwstat> WstatAsync(string[] relativePath, Twstat msg, CancellationToken ct)
    {
        return await WithCancellation(msg.Tag, ct, async (token) =>
        {
            var node = await ResolveNode(relativePath, token);
            await node.WstatAsync(msg.Stat, token);
            return new Rwstat(msg.Tag);
        });
    }

    public async Task<Rwstat> WstatAsync(string[] relativePath, Twstat twstat, NinePDialect dialect, CancellationToken ct = default)
        => await WstatAsync(relativePath, twstat, ct);

    public async Task<Rremove> RemoveAsync(string[] relativePath, Tremove msg, CancellationToken ct)
    {
        return await WithCancellation(msg.Tag, ct, async (token) =>
        {
            if (relativePath.Length == 0)
            {
                throw new InvalidOperationException("Cannot remove root.");
            }

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
        => await CreateCoreAsync(parentPath, msg, Dialect, ct);

    public async Task<Rcreate> CreateAsync(string[] parentRelativePath, Tcreate tcreate, NinePDialect dialect, CancellationToken ct = default)
        => await CreateCoreAsync(parentRelativePath, tcreate, dialect, ct);

    public async Task<Rsymlink> SymlinkAsync(string[] relativePath, Tsymlink msg, CancellationToken ct)
        => await SymlinkAsync(relativePath, msg, Dialect, ct);

    public async Task<Rreadlink> ReadlinkAsync(string[] relativePath, Treadlink msg, CancellationToken ct)
    {
        return await WithCancellation(msg.Tag, ct, async (token) =>
        {
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
        return await WithCancellation(msg.Tag, ct, async (token) =>
        {
            var node = await ResolveNode(relativePath, token);
            return await node.LockAsync(msg, token);
        });
    }

    public async Task<Rgetlock> GetlockAsync(string[] relativePath, Tgetlock msg, CancellationToken ct)
    {
        return await WithCancellation(msg.Tag, ct, async (token) =>
        {
            var node = await ResolveNode(relativePath, token);
            return await node.GetlockAsync(msg, token);
        });
    }

    public async Task<Rxattrwalk> XattrwalkAsync(string[] relativePath, Txattrwalk msg, CancellationToken ct)
    {
        return await WithCancellation(msg.Tag, ct, async (token) =>
        {
            var node = await ResolveNode(relativePath, token);
            return await node.XattrwalkAsync(msg, token);
        });
    }

    public async Task<Rxattrcreate> XattrcreateAsync(string[] relativePath, Txattrcreate msg, CancellationToken ct)
    {
        return await WithCancellation(msg.Tag, ct, async (token) =>
        {
            var node = await ResolveNode(relativePath, token);
            return await node.XattrcreateAsync(msg, token);
        });
    }

    public Task<Rflush> FlushAsync(Tflush msg, CancellationToken ct)
    {
        if (inFlightRequests.TryGetValue(msg.OldTag, out var innerCts))
        {
            innerCts.Cancel();
        }

        return Task.FromResult(new Rflush(msg.Tag));
    }

    public async Task<Rreaddir> ReaddirAsync(string[] relativePath, Treaddir msg, CancellationToken ct)
        => await ReaddirCoreAsync(relativePath, msg, Dialect, ct);

    public Task<Rreaddir> ReaddirAsync(string[] relativePath, Treaddir treaddir, NinePDialect dialect, CancellationToken ct = default)
        => ReaddirCoreAsync(relativePath, treaddir, dialect, ct);

    public Task<Rreaddir> ReaddirCompatAsync(string[] relativePath, Treaddir treaddir, NinePDialect dialect, CancellationToken ct = default)
        => ReaddirCoreAsync(relativePath, treaddir, dialect, ct);

    private async Task<T> WithCancellation<T>(ushort tag, CancellationToken ct, Func<CancellationToken, Task<T>> action)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (!inFlightRequests.TryAdd(tag, cts))
        {
             inFlightRequests[tag].Cancel();
             inFlightRequests[tag] = cts;
        }

        try
        {
            return await action(cts.Token);
        }
        finally
        {
            if (inFlightRequests.TryGetValue(tag, out var currentCts) && currentCts == cts)
            {
                inFlightRequests.TryRemove(tag, out _);
            }

            cts.Dispose();
        }
    }

    private async Task<INinePNode> ResolveNode(string[] relativePath, CancellationToken ct)
    {
        INinePNode current = root;
        foreach (var segment in relativePath)
        {
            if (string.IsNullOrEmpty(segment) || segment == ".")
            {
                continue;
            }

            var next = await current.WalkAsync(segment, ct);
            if (next == null)
            {
                throw new InvalidOperationException($"Path not found: {segment}");
            }

            current = next;
        }

        return current;
    }

    private async Task<Rwalk> WalkAsync(string[] relativePath, Twalk msg, NinePDialect dialect, CancellationToken ct)
    {
        return await WithCancellation(msg.Tag, ct, async (token) =>
        {
            var wqids = new List<Qid>();
            INinePNode current = await ResolveNode(relativePath, token);
            foreach (var segment in msg.Wname)
            {
                var next = await current.WalkAsync(segment, token);
                if (next == null)
                {
                    break;
                }

                wqids.Add(next.GetStat(dialect).Qid);
                current = next;
            }

            return new Rwalk(msg.Tag, wqids.ToArray());
        });
    }

    private async Task<Ropen> OpenCoreAsync(string[] relativePath, Topen msg, NinePDialect dialect, CancellationToken ct)
    {
        return await WithCancellation(msg.Tag, ct, async (token) =>
        {
            var node = await ResolveNode(relativePath, token);
            return new Ropen(msg.Tag, node.GetStat(dialect).Qid, 0);
        });
    }

    private async Task<Rstat> StatCoreAsync(string[] relativePath, Tstat msg, NinePDialect dialect, CancellationToken ct)
    {
        return await WithCancellation(msg.Tag, ct, async (token) =>
        {
            var node = await ResolveNode(relativePath, token);
            return new Rstat(msg.Tag, node.GetStat(dialect));
        });
    }

    private async Task<Rcreate> CreateCoreAsync(string[] parentPath, Tcreate msg, NinePDialect dialect, CancellationToken ct)
    {
        return await WithCancellation(msg.Tag, ct, async (token) =>
        {
            var parent = await ResolveNode(parentPath, token);
            var newNode = await parent.CreateAsync(msg.Name, msg.Perm, msg.Mode, token);
            return new Rcreate(msg.Tag, newNode.GetStat(dialect).Qid, 0);
        });
    }

    private async Task<Rsymlink> SymlinkAsync(string[] relativePath, Tsymlink msg, NinePDialect dialect, CancellationToken ct)
    {
        return await WithCancellation(msg.Tag, ct, async (token) =>
        {
            var parent = await ResolveNode(relativePath, token);
            await parent.SymlinkAsync(msg.Name, msg.Symtgt, token);
            var linkNode = await parent.WalkAsync(msg.Name, token);
            uint size = (uint)(NinePConstants.HeaderSize + 13);
            return new Rsymlink(size, msg.Tag, linkNode!.GetStat(dialect).Qid);
        });
    }

    private async Task<Rreaddir> ReaddirCoreAsync(string[] relativePath, Treaddir msg, NinePDialect dialect, CancellationToken ct)
    {
        return await WithCancellation(msg.Tag, ct, async (token) =>
        {
            var node = await ResolveNode(relativePath, token);
            var entries = await node.ReaddirAsync(token);

            var fullBuffer = new List<byte>();
            ulong currentOffset = 0;
            foreach (var entry in entries)
            {
                var stat = entry.GetStat(dialect);
                var nameBytes = System.Text.Encoding.UTF8.GetBytes(stat.Name);
                var entryBuffer = new byte[13 + 8 + 1 + 2 + nameBytes.Length];
                int offset = 0;
                var span = entryBuffer.AsSpan();
                span.WriteQid(stat.Qid, ref offset);
                currentOffset += (ulong)entryBuffer.Length;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), currentOffset);
                offset += 8;
                span[offset++] = (byte)stat.Qid.Type;
                span.WriteString(stat.Name, ref offset);
                fullBuffer.AddRange(entryBuffer);
            }

            var resultData = fullBuffer.Skip((int)msg.Offset).Take((int)msg.Count).ToArray();
            uint totalSize = (uint)(NinePConstants.HeaderSize + 4 + resultData.Length);
            return new Rreaddir(totalSize, msg.Tag, (uint)resultData.Length, resultData);
        });
    }
}
