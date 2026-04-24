using NinePSharp.Constants;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Protocol;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

namespace NinePSharp.Tests.Helpers;

internal abstract class TestHandlerBase : INinePRequestHandler
{
    public virtual Task<IAuthHandler?> GetAuthHandlerAsync(Tauth msg, CancellationToken ct) => Task.FromResult<IAuthHandler?>(null);
    public virtual Task<Rattach> AttachAsync(Tattach msg, CancellationToken ct) => Task.FromResult(new Rattach(msg.Tag, new Qid(QidType.QTDIR, 0, 0)));
    public abstract Task<Rwalk> WalkAsync(string[] relativePath, Twalk msg, CancellationToken ct);
    public abstract Task<Ropen> OpenAsync(string[] relativePath, Topen msg, CancellationToken ct);
    public abstract Task<Rread> ReadAsync(string[] relativePath, Tread msg, CancellationToken ct);
    public abstract Task<Rwrite> WriteAsync(string[] relativePath, Twrite msg, CancellationToken ct);
    public virtual Task<Rclunk> ClunkAsync(string[] relativePath, Tclunk msg, CancellationToken ct) => Task.FromResult(new Rclunk(msg.Tag));
    public abstract Task<Rstat> StatAsync(string[] relativePath, Tstat msg, CancellationToken ct);
    public virtual Task<Rwstat> WstatAsync(string[] relativePath, Twstat msg, CancellationToken ct) => throw new NotImplementedException();
    public virtual Task<Rcreate> CreateAsync(string[] parentPath, Tcreate msg, CancellationToken ct) => throw new NotImplementedException();
    public virtual Task<Rremove> RemoveAsync(string[] relativePath, Tremove msg, CancellationToken ct) => throw new NotImplementedException();
    public virtual Task<Rreaddir>? ReaddirAsync(string[] relativePath, Treaddir msg, CancellationToken ct) => null;

    public virtual Task<Rsymlink> SymlinkAsync(string[] relativePath, Tsymlink msg, CancellationToken ct) => throw new NotImplementedException();
    public virtual Task<Rreadlink> ReadlinkAsync(string[] relativePath, Treadlink msg, CancellationToken ct) => throw new NotImplementedException();
    public virtual Task<Rlink> LinkAsync(string[] relativePath, Tlink msg, CancellationToken ct) => throw new NotImplementedException();

    public virtual Task<Rlerror> LockAsync(string[] relativePath, Tlock msg, CancellationToken ct) => throw new NotImplementedException();
    public virtual Task<Rgetlock> GetlockAsync(string[] relativePath, Tgetlock msg, CancellationToken ct) => throw new NotImplementedException();
    public virtual Task<Rxattrwalk> XattrwalkAsync(string[] relativePath, Txattrwalk msg, CancellationToken ct) => throw new NotImplementedException();
    public virtual Task<Rxattrcreate> XattrcreateAsync(string[] relativePath, Txattrcreate msg, CancellationToken ct) => throw new NotImplementedException();

    public virtual Task<Rflush> FlushAsync(Tflush msg, CancellationToken ct) => Task.FromResult(new Rflush(msg.Tag));
}

internal static class DispatcherIntegrationTestKit
{
    internal readonly record struct ReaddirEntry(QidType QidType, ulong NextOffset, string Name);

    internal static NinePFSDispatcher CreateDispatcher(INinePRequestHandler handler)
    {
        return new NinePFSDispatcher(
            NullLogger<NinePFSDispatcher>.Instance,
            new[] { new NinePSharp.Server.FileSystem.FileSystemProtocolBackend(new NinePSharp.Server.FileSystem.NinePDir("/")) }, // Dummy
            new Mock<IRemoteMountProvider>().Object); // Use dummy for now since we disabled the engine
    }

    internal static async Task AttachRootAsync(NinePFSDispatcher dispatcher, ushort tag, uint fid)
        => await AttachAsync(dispatcher, tag, fid, "/");

    internal static async Task AttachAsync(NinePFSDispatcher dispatcher, ushort tag, uint fid, string aname)
    {
        var response = await dispatcher.DispatchAsync(
            "test-session",
            NinePMessage.NewMsgTattach(new Tattach(tag, fid, NinePConstants.NoFid, "user", aname)),
            dialect: NinePDialect.NineP2000);

        if (response is not Rattach)
        {
            string errMsg = response is Rerror err ? err.Ename : "";
            throw new Xunit.Sdk.XunitException($"Expected Rattach, got {response.GetType().Name} ({errMsg})");
        }
    }

    internal static async Task<Rwalk> WalkAsync(NinePFSDispatcher dispatcher, ushort tag, uint fid, uint newFid, string[] wname)
    {
        var response = await dispatcher.DispatchAsync(
            "test-session",
            NinePMessage.NewMsgTwalk(new Twalk(tag, fid, newFid, wname)),
            dialect: NinePDialect.NineP2000);

        if (response is not Rwalk walk)
        {
            string errMsg = response is Rerror err ? err.Ename : "";
            throw new Xunit.Sdk.XunitException($"Expected Rwalk, got {response.GetType().Name} ({errMsg})");
        }

        return walk;
    }

    internal static async Task<Rread> ReadAsync(NinePFSDispatcher dispatcher, ushort tag, uint fid, ulong offset, uint count)
    {
        var response = await dispatcher.DispatchAsync(
            "test-session",
            NinePMessage.NewMsgTread(new Tread(tag, fid, offset, count)),
            dialect: NinePDialect.NineP2000);

        if (response is not Rread read)
        {
            string errMsg = response is Rerror err ? err.Ename : "";
            throw new Xunit.Sdk.XunitException($"Expected Rread, got {response.GetType().Name} ({errMsg})");
        }

        return read;
    }

    internal static async Task<Rreaddir> ReaddirAsync(NinePFSDispatcher dispatcher, ushort tag, uint fid, ulong offset, uint count)
    {
        var response = await dispatcher.DispatchAsync(
            "test-session",
            NinePMessage.NewMsgTreaddir(new Treaddir(24, tag, fid, offset, count)),
            dialect: NinePDialect.NineP2000);

        if (response is not Rreaddir readdir)
        {
            var errMsg = response is Rerror err ? $": {err.Ename}" : "";
            throw new Xunit.Sdk.XunitException($"Expected Rreaddir, got {response.GetType().Name}{errMsg}");
        }

        return readdir;
    }

    internal static async Task<Rwrite> WriteAsync(NinePFSDispatcher dispatcher, ushort tag, uint fid, ulong offset, byte[] data)
    {
        var response = await dispatcher.DispatchAsync(
            "test-session",
            NinePMessage.NewMsgTwrite(new Twrite(tag, fid, offset, data)),
            dialect: NinePDialect.NineP2000);

        if (response is not Rwrite write)
        {
            string errMsg = response is Rerror err ? err.Ename : "";
            throw new Xunit.Sdk.XunitException($"Expected Rwrite, got {response.GetType().Name} ({errMsg})");
        }

        return write;
    }

    internal static async Task<Ropen> OpenAsync(NinePFSDispatcher dispatcher, ushort tag, uint fid, byte mode = 0)
    {
        var response = await dispatcher.DispatchAsync(
            "test-session",
            NinePMessage.NewMsgTopen(new Topen(tag, fid, mode)),
            dialect: NinePDialect.NineP2000);

        if (response is not Ropen open)
        {
            string errMsg = response is Rerror err ? err.Ename : "";
            throw new Xunit.Sdk.XunitException($"Expected Ropen, got {response.GetType().Name} ({errMsg})");
        }

        return open;
    }

    internal static async Task<Rcreate> CreateAsync(NinePFSDispatcher dispatcher, ushort tag, uint fid, string name, uint perm = 0644, byte mode = 0)
    {
        var response = await dispatcher.DispatchAsync(
            "test-session",
            NinePMessage.NewMsgTcreate(new Tcreate(tag, fid, name, perm, mode)),
            dialect: NinePDialect.NineP2000);

        if (response is not Rcreate create)
        {
            string errMsg = response is Rerror err ? err.Ename : "";
            throw new Xunit.Sdk.XunitException($"Expected Rcreate, got {response.GetType().Name} ({errMsg})");
        }

        return create;
    }

    internal static async Task<Rstat> StatAsync(NinePFSDispatcher dispatcher, ushort tag, uint fid)
    {
        var response = await dispatcher.DispatchAsync(
            "test-session",
            NinePMessage.NewMsgTstat(new Tstat(tag, fid)),
            dialect: NinePDialect.NineP2000);

        if (response is not Rstat stat)
        {
            string errMsg = response is Rerror err ? err.Ename : "";
            throw new Xunit.Sdk.XunitException($"Expected Rstat, got {response.GetType().Name} ({errMsg})");
        }

        return stat;
    }

    internal static string ReadPayload(Rread read) => Encoding.UTF8.GetString(read.Data.Span);

    internal static List<Stat> ParseStatsTable(ReadOnlySpan<byte> data)
    {
        var result = new List<Stat>();
        int offset = 0;

        while (offset < data.Length)
        {
            result.Add(new Stat(data, ref offset));
        }

        return result;
    }

    internal static string CleanMount(string? raw, int index)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return $"m{index}";
        }

        var chars = raw
            .Where(c => char.IsLetterOrDigit(c) || c is '_' or '-')
            .Take(24)
            .ToArray();

        return chars.Length == 0 ? $"m{index}" : new string(chars);
    }
}

internal sealed class MarkerFileSystem : TestHandlerBase
{
    private readonly string _marker;

    internal MarkerFileSystem(string marker)
    {
        _marker = marker;
    }

    public override Task<Rwalk> WalkAsync(string[] relativePath, Twalk msg, CancellationToken ct)
    {
        var qids = msg.Wname.Select((_, i) => new Qid(QidType.QTFILE, 0, (ulong)(_marker.GetHashCode() + i + 1))).ToArray();
        return Task.FromResult(new Rwalk(msg.Tag, qids));
    }

    public override Task<Ropen> OpenAsync(string[] relativePath, Topen msg, CancellationToken ct)
    {
        return Task.FromResult(new Ropen(msg.Tag, new Qid(QidType.QTFILE, 0, (ulong)_marker.GetHashCode()), 0));
    }

    public override Task<Rread> ReadAsync(string[] relativePath, Tread msg, CancellationToken ct)
    {
        return Task.FromResult(new Rread(msg.Tag, Encoding.UTF8.GetBytes(_marker)));
    }

    public override Task<Rwrite> WriteAsync(string[] relativePath, Twrite msg, CancellationToken ct) => NotSupported<Rwrite>();
    public override Task<Rstat> StatAsync(string[] relativePath, Tstat msg, CancellationToken ct) => Task.FromResult(new Rstat(msg.Tag, new Stat(0,0,0, new Qid(QidType.QTFILE, 0, 0), 0755, 0, 0, 0, "", "", "", "", NinePDialect.NineP2000)));

    private static Task<T> NotSupported<T>()
    {
        return Task.FromException<T>(new Exception("Operation is not supported by MarkerFileSystem"));
    }
}

internal sealed class CreateTrackingFileSystem : TestHandlerBase
{
    private readonly string _marker;
    private readonly List<string> _created = new();

    internal CreateTrackingFileSystem(string marker)
    {
        _marker = marker;
    }

    public override Task<Rwalk> WalkAsync(string[] relativePath, Twalk twalk, CancellationToken ct)
    {
        var qids = twalk.Wname.Select((_, i) => new Qid(QidType.QTFILE, 0, (ulong)(_marker.GetHashCode() + i + 1))).ToArray();
        return Task.FromResult(new Rwalk(twalk.Tag, qids));
    }

    public override Task<Ropen> OpenAsync(string[] relativePath, Topen topen, CancellationToken ct)
        => Task.FromResult(new Ropen(topen.Tag, new Qid(QidType.QTFILE, 0, (ulong)_marker.GetHashCode()), 0));

    public override Task<Rread> ReadAsync(string[] relativePath, Tread tread, CancellationToken ct)
    {
        var payload = _created.Count == 0 ? _marker : string.Join(",", _created);
        return Task.FromResult(new Rread(tread.Tag, Encoding.UTF8.GetBytes(payload)));
    }

    public override Task<Rcreate> CreateAsync(string[] relativePath, Tcreate tcreate, CancellationToken ct)
    {
        _created.Add(tcreate.Name);
        ulong path = (ulong)Math.Abs((_marker + ":" + tcreate.Name).GetHashCode());
        return Task.FromResult(new Rcreate(tcreate.Tag, new Qid(QidType.QTFILE, 0, path), 8192));
    }

    public override Task<Rwrite> WriteAsync(string[] relativePath, Twrite twrite, CancellationToken ct) => Task.FromResult(new Rwrite(twrite.Tag, (uint)twrite.Data.Length));
    public override Task<Rstat> StatAsync(string[] relativePath, Tstat tstat, CancellationToken ct) => Task.FromResult(new Rstat(tstat.Tag, new Stat(0,0,0, new Qid(QidType.QTFILE, 0, 0), 0755, 0, 0, 0, "", "", "", "", NinePDialect.NineP2000)));
}

internal sealed class DirectoryListingFileSystem : TestHandlerBase
{
    private readonly string[] _entries;

    internal DirectoryListingFileSystem(IEnumerable<string> entries)
    {
        _entries = entries.ToArray();
    }

    public override Task<Rwalk> WalkAsync(string[] relativePath, Twalk twalk, CancellationToken ct)
    {
        var qids = twalk.Wname.Select((name, i) => new Qid(QidType.QTDIR, 0, (ulong)Math.Abs((name + i).GetHashCode()))).ToArray();
        return Task.FromResult(new Rwalk(twalk.Tag, qids));
    }

    public override Task<Ropen> OpenAsync(string[] relativePath, Topen topen, CancellationToken ct)
        => Task.FromResult(new Ropen(topen.Tag, new Qid(QidType.QTDIR, 0, 1), 8192));

    public override Task<Rread> ReadAsync(string[] relativePath, Tread tread, CancellationToken ct)
    {
        var allStats = new List<byte>();
        foreach (var name in _entries)
        {
            var qid = new Qid(QidType.QTDIR, 0, (ulong)Math.Abs(name.GetHashCode()));
            var stat = new Stat(0, 0, 0, qid, (uint)NinePConstants.FileMode9P.DMDIR | 0755, 0, 0, 0, name, "none", "none", "none", NinePDialect.NineP2000);
            var buffer = new byte[stat.Size];
            int off = 0;
            stat.WriteTo(buffer, ref off);
            allStats.AddRange(buffer);
        }

        if (tread.Offset >= (ulong)allStats.Count) return Task.FromResult(new Rread(tread.Tag, Array.Empty<byte>()));
        int start = (int)tread.Offset;
        int len = (int)Math.Min(tread.Count, (uint)(allStats.Count - start));
        return Task.FromResult(new Rread(tread.Tag, allStats.GetRange(start, len).ToArray()));
    }

    public override Task<Rwrite> WriteAsync(string[] relativePath, Twrite twrite, CancellationToken ct) => Task.FromResult(new Rwrite(twrite.Tag, (uint)twrite.Data.Length));
    public override Task<Rstat> StatAsync(string[] relativePath, Tstat tstat, CancellationToken ct) => Task.FromResult(new Rstat(tstat.Tag, new Stat(0,0,0, new Qid(QidType.QTFILE, 0, 0), 0755, 0, 0, 0, "", "", "", "", NinePDialect.NineP2000)));
}

internal sealed class ExistingPathFileSystem : TestHandlerBase
{
    private readonly HashSet<string> _paths;

    internal ExistingPathFileSystem(IEnumerable<string> paths)
    {
        _paths = new HashSet<string>(paths.Select(NormalizePath), StringComparer.Ordinal) { "/" };
    }

    public override Task<Rwalk> WalkAsync(string[] relativePath, Twalk twalk, CancellationToken ct)
    {
        var temp = new List<string>(relativePath);
        var qids = new List<Qid>();

        foreach (var segment in twalk.Wname)
        {
            if (segment == "..")
            {
                if (temp.Count > 0)
                {
                    temp.RemoveAt(temp.Count - 1);
                }
            }
            else if (segment != ".")
            {
                temp.Add(segment);
            }

            string path = Normalize(temp);
            if (!_paths.Contains(path))
            {
                return Task.FromResult(new Rwalk(twalk.Tag, qids.ToArray()));
            }

            qids.Add(new Qid(QidType.QTFILE, 0, (ulong)Math.Abs(path.GetHashCode())));
        }

        return Task.FromResult(new Rwalk(twalk.Tag, qids.ToArray()));
    }

    public override Task<Ropen> OpenAsync(string[] relativePath, Topen topen, CancellationToken ct)
        => Task.FromResult(new Ropen(topen.Tag, new Qid(QidType.QTFILE, 0, 1), 8192));

    public override Task<Rread> ReadAsync(string[] relativePath, Tread tread, CancellationToken ct)
        => Task.FromResult(new Rread(tread.Tag, Encoding.UTF8.GetBytes(Normalize(relativePath))));

    public override Task<Rwrite> WriteAsync(string[] relativePath, Twrite twrite, CancellationToken ct) => Task.FromResult(new Rwrite(twrite.Tag, (uint)twrite.Data.Length));
    public override Task<Rstat> StatAsync(string[] relativePath, Tstat tstat, CancellationToken ct) => Task.FromResult(new Rstat(tstat.Tag, new Stat(0,0,0, new Qid(QidType.QTFILE, 0, 0), 0755, 0, 0, 0, "", "", "", "", NinePDialect.NineP2000)));

    private static string Normalize(IEnumerable<string> segments)
    {
        var list = segments.ToList();
        return list.Count == 0 ? "/" : "/" + string.Join("/", list);
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
        {
            return "/";
        }

        return Normalize(path.Split('/', StringSplitOptions.RemoveEmptyEntries));
    }
}

internal sealed class SharedMutableFileSystem : TestHandlerBase
{
    private sealed class SharedState
    {
        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal)
        {
            ["/"] = Array.Empty<byte>()
        };
    }

    private readonly SharedState _state;

    internal SharedMutableFileSystem()
    {
        _state = new SharedState();
    }

    private string GetFullPath(string[] rel) => rel.Length == 0 ? "/" : "/" + string.Join("/", rel);

    public override Task<Rwalk> WalkAsync(string[] relativePath, Twalk twalk, CancellationToken ct)
    {
        var tempPath = new List<string>(relativePath);
        var qids = new List<Qid>();

        foreach (var name in twalk.Wname)
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

            string path = tempPath.Count == 0 ? "/" : "/" + string.Join("/", tempPath);
            var qidType = _state.Files.ContainsKey(path) ? QidType.QTFILE : QidType.QTDIR;
            qids.Add(new Qid(qidType, 0, (ulong)Math.Abs(path.GetHashCode())));
        }

        return Task.FromResult(new Rwalk(twalk.Tag, qids.ToArray()));
    }

    public override Task<Ropen> OpenAsync(string[] relativePath, Topen topen, CancellationToken ct)
    {
        string path = GetFullPath(relativePath);
        var qidType = _state.Files.ContainsKey(path) ? QidType.QTFILE : QidType.QTDIR;
        return Task.FromResult(new Ropen(topen.Tag, new Qid(qidType, 0, (ulong)Math.Abs(path.GetHashCode())), 8192));
    }

    public override Task<Rread> ReadAsync(string[] relativePath, Tread tread, CancellationToken ct)
    {
        string path = GetFullPath(relativePath);
        if (!_state.Files.TryGetValue(path, out var data))
        {
            return Task.FromResult(new Rread(tread.Tag, Array.Empty<byte>()));
        }

        if (tread.Offset >= (ulong)data.Length)
        {
            return Task.FromResult(new Rread(tread.Tag, Array.Empty<byte>()));
        }

        int offset = (int)tread.Offset;
        int count = Math.Min((int)tread.Count, data.Length - offset);
        return Task.FromResult(new Rread(tread.Tag, data.AsSpan(offset, count).ToArray()));
    }

    public override Task<Rwrite> WriteAsync(string[] relativePath, Twrite twrite, CancellationToken ct)
    {
        string path = GetFullPath(relativePath);
        if (!_state.Files.TryGetValue(path, out var existing))
        {
            existing = Array.Empty<byte>();
        }

        int offset = (int)twrite.Offset;
        byte[] incoming = twrite.Data.ToArray();
        byte[] content = new byte[Math.Max(existing.Length, offset + incoming.Length)];
        existing.CopyTo(content, 0);
        incoming.CopyTo(content, offset);
        _state.Files[path] = content;
        return Task.FromResult(new Rwrite(twrite.Tag, (uint)incoming.Length));
    }

    public override Task<Rcreate> CreateAsync(string[] relativePath, Tcreate tcreate, CancellationToken ct)
    {
        string parent = GetFullPath(relativePath);
        string path = parent == "/" ? "/" + tcreate.Name : parent + "/" + tcreate.Name;
        _state.Files[path] = Array.Empty<byte>();
        return Task.FromResult(new Rcreate(tcreate.Tag, new Qid(QidType.QTFILE, 0, (ulong)Math.Abs(path.GetHashCode())), 8192));
    }

    public override Task<Rstat> StatAsync(string[] relativePath, Tstat tstat, CancellationToken ct) => Task.FromResult(new Rstat(tstat.Tag, new Stat(0,0,0, new Qid(QidType.QTFILE, 0, 0), 0755, 0, 0, 0, "", "", "", "", NinePDialect.NineP2000)));
}
