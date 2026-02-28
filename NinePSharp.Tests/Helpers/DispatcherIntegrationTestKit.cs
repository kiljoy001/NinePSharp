using NinePSharp.Constants;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
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

internal static class DispatcherIntegrationTestKit
{
    internal readonly record struct ReaddirEntry(QidType QidType, ulong NextOffset, byte TypeByte, string Name);

    internal static NinePFSDispatcher CreateDispatcher(IEnumerable<IProtocolBackend> backends)
    {
        return new NinePFSDispatcher(
            NullLogger<NinePFSDispatcher>.Instance,
            backends,
            new NullRemoteMountProvider());
    }

    internal static async Task AttachRootAsync(NinePFSDispatcher dispatcher, ushort tag, uint fid)
        => await AttachAsync(dispatcher, tag, fid, "/");

    internal static async Task AttachAsync(NinePFSDispatcher dispatcher, ushort tag, uint fid, string aname)
    {
        var response = await dispatcher.DispatchAsync(
            NinePMessage.NewMsgTattach(new Tattach(tag, fid, NinePConstants.NoFid, "user", aname)),
            dialect: NinePDialect.NineP2000U);

        if (response is not Rattach)
        {
            throw new Xunit.Sdk.XunitException($"Expected Rattach, got {response.GetType().Name}");
        }
    }

    internal static async Task<Rwalk> WalkAsync(NinePFSDispatcher dispatcher, ushort tag, uint fid, uint newFid, string[] wname)
    {
        var response = await dispatcher.DispatchAsync(
            NinePMessage.NewMsgTwalk(new Twalk(tag, fid, newFid, wname)),
            dialect: NinePDialect.NineP2000U);

        if (response is not Rwalk walk)
        {
            throw new Xunit.Sdk.XunitException($"Expected Rwalk, got {response.GetType().Name}");
        }

        return walk;
    }

    internal static async Task<Rread> ReadAsync(NinePFSDispatcher dispatcher, ushort tag, uint fid, ulong offset, uint count)
    {
        var response = await dispatcher.DispatchAsync(
            NinePMessage.NewMsgTread(new Tread(tag, fid, offset, count)),
            dialect: NinePDialect.NineP2000U);

        if (response is not Rread read)
        {
            throw new Xunit.Sdk.XunitException($"Expected Rread, got {response.GetType().Name}");
        }

        return read;
    }

    internal static async Task<Rwrite> WriteAsync(NinePFSDispatcher dispatcher, ushort tag, uint fid, ulong offset, byte[] data)
    {
        var response = await dispatcher.DispatchAsync(
            NinePMessage.NewMsgTwrite(new Twrite(tag, fid, offset, data)),
            dialect: NinePDialect.NineP2000U);

        if (response is not Rwrite write)
        {
            throw new Xunit.Sdk.XunitException($"Expected Rwrite, got {response.GetType().Name}");
        }

        return write;
    }

    internal static async Task<Ropen> OpenAsync(NinePFSDispatcher dispatcher, ushort tag, uint fid, byte mode = 0)
    {
        var response = await dispatcher.DispatchAsync(
            NinePMessage.NewMsgTopen(new Topen(tag, fid, mode)),
            dialect: NinePDialect.NineP2000U);

        if (response is not Ropen open)
        {
            throw new Xunit.Sdk.XunitException($"Expected Ropen, got {response.GetType().Name}");
        }

        return open;
    }

    internal static async Task<Rcreate> CreateAsync(NinePFSDispatcher dispatcher, ushort tag, uint fid, string name, uint perm = 0644, byte mode = 0)
    {
        var response = await dispatcher.DispatchAsync(
            NinePMessage.NewMsgTcreate(BuildTcreate(tag, fid, name, perm, mode)),
            dialect: NinePDialect.NineP2000U);

        if (response is not Rcreate create)
        {
            throw new Xunit.Sdk.XunitException($"Expected Rcreate, got {response.GetType().Name}");
        }

        return create;
    }

    private static Tcreate BuildTcreate(ushort tag, uint fid, string name, uint perm, byte mode)
    {
        int nameLen = Encoding.UTF8.GetByteCount(name);
        uint size = (uint)(NinePConstants.HeaderSize + 4 + 2 + nameLen + 4 + 1);
        byte[] data = new byte[size];

        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), size);
        data[4] = (byte)MessageTypes.Tcreate;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(5, 2), tag);

        int offset = NinePConstants.HeaderSize;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), fid);
        offset += 4;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset, 2), (ushort)nameLen);
        offset += 2;
        Encoding.UTF8.GetBytes(name).CopyTo(data.AsSpan(offset, nameLen));
        offset += nameLen;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), perm);
        offset += 4;
        data[offset] = mode;

        return new Tcreate(data);
    }

    internal static async Task<Rstat> StatAsync(NinePFSDispatcher dispatcher, ushort tag, uint fid)
    {
        var response = await dispatcher.DispatchAsync(
            NinePMessage.NewMsgTstat(new Tstat(tag, fid)),
            dialect: NinePDialect.NineP2000U);

        if (response is not Rstat stat)
        {
            throw new Xunit.Sdk.XunitException($"Expected Rstat, got {response.GetType().Name}");
        }

        return stat;
    }

    internal static async Task<Rreaddir> ReaddirAsync(NinePFSDispatcher dispatcher, ushort tag, uint fid, ulong offset, uint count)
    {
        var response = await dispatcher.DispatchAsync(
            NinePMessage.NewMsgTreaddir(new Treaddir(0, tag, fid, offset, count)),
            dialect: NinePDialect.NineP2000U);

        if (response is not Rreaddir readdir)
        {
            throw new Xunit.Sdk.XunitException($"Expected Rreaddir, got {response.GetType().Name}");
        }

        return readdir;
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

    internal static List<ReaddirEntry> ParseReaddirEntries(ReadOnlySpan<byte> data)
    {
        var result = new List<ReaddirEntry>();
        int offset = 0;

        while (offset < data.Length)
        {
            if (data.Length - offset < 24)
            {
                throw new Xunit.Sdk.XunitException($"Malformed readdir entry at byte offset {offset}");
            }

            var qidType = (QidType)data[offset];
            offset += 1 + 4 + 8; // qid type + version + path

            ulong nextOffset = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8));
            offset += 8;

            byte typeByte = data[offset++];
            ushort nameLen = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
            offset += 2;

            if (nameLen > data.Length - offset)
            {
                throw new Xunit.Sdk.XunitException($"Invalid name length {nameLen} at byte offset {offset}");
            }

            string name = Encoding.UTF8.GetString(data.Slice(offset, nameLen));
            offset += nameLen;

            result.Add(new ReaddirEntry(qidType, nextOffset, typeByte, name));
        }

        return result;
    }
}

internal sealed class StubBackend : IProtocolBackend
{
    private readonly Func<INinePFileSystem> _factory;

    internal StubBackend(string mountPath, Func<INinePFileSystem> factory)
    {
        MountPath = mountPath;
        _factory = factory;
    }

    public string Name => MountPath.Trim('/');
    public string MountPath { get; }

    public Task InitializeAsync(IConfiguration configuration) => Task.CompletedTask;

    public INinePFileSystem GetFileSystem(X509Certificate2? certificate = null) => _factory();

    public INinePFileSystem GetFileSystem(SecureString? credentials, X509Certificate2? certificate = null) => _factory();
}

internal sealed class MarkerFileSystem : INinePFileSystem
{
    private readonly string _marker;

    internal MarkerFileSystem(string marker)
    {
        _marker = marker;
    }

    public bool Dialect { get; set; }

    public Task<Rwalk> WalkAsync(Twalk twalk)
    {
        var qids = twalk.Wname.Select((_, i) => new Qid(QidType.QTFILE, 0, (ulong)(_marker.GetHashCode() + i + 1))).ToArray();
        return Task.FromResult(new Rwalk(twalk.Tag, qids));
    }

    public Task<Ropen> OpenAsync(Topen topen)
    {
        return Task.FromResult(new Ropen(topen.Tag, new Qid(QidType.QTFILE, 0, (ulong)_marker.GetHashCode()), 0));
    }

    public Task<Rread> ReadAsync(Tread tread)
    {
        return Task.FromResult(new Rread(tread.Tag, Encoding.UTF8.GetBytes(_marker)));
    }

    public Task<Rwrite> WriteAsync(Twrite twrite) => NotSupported<Rwrite>();

    public Task<Rclunk> ClunkAsync(Tclunk tclunk) => Task.FromResult(new Rclunk(tclunk.Tag));

    public Task<Rstat> StatAsync(Tstat tstat) => NotSupported<Rstat>();

    public Task<Rwstat> WstatAsync(Twstat twstat) => NotSupported<Rwstat>();

    public Task<Rremove> RemoveAsync(Tremove tremove) => NotSupported<Rremove>();

    public Task<Rgetattr> GetAttrAsync(Tgetattr tgetattr) => NotSupported<Rgetattr>();

    public Task<Rsetattr> SetAttrAsync(Tsetattr tsetattr) => NotSupported<Rsetattr>();

    public Task<Rcreate> CreateAsync(Tcreate tcreate) => NotSupported<Rcreate>();

    public Task<Rstatfs> StatfsAsync(Tstatfs tstatfs) => NotSupported<Rstatfs>();

    public Task<Rlopen> LopenAsync(Tlopen tlopen) => NotSupported<Rlopen>();

    public Task<Rlcreate> LcreateAsync(Tlcreate tlcreate) => NotSupported<Rlcreate>();

    public Task<Rsymlink> SymlinkAsync(Tsymlink tsymlink) => NotSupported<Rsymlink>();

    public Task<Rmknod> MknodAsync(Tmknod tmknod) => NotSupported<Rmknod>();

    public Task<Rrename> RenameAsync(Trename trename) => NotSupported<Rrename>();

    public Task<Rreadlink> ReadlinkAsync(Treadlink treadlink) => NotSupported<Rreadlink>();

    public Task<Rxattrwalk> XattrwalkAsync(Txattrwalk txattrwalk) => NotSupported<Rxattrwalk>();

    public Task<Rxattrcreate> XattrcreateAsync(Txattrcreate txattrcreate) => NotSupported<Rxattrcreate>();

    public Task<Rreaddir> ReaddirAsync(Treaddir treaddir) => NotSupported<Rreaddir>();

    public Task<Rfsync> FsyncAsync(Tfsync tfsync) => NotSupported<Rfsync>();

    public Task<Rlock> LockAsync(Tlock tlock) => NotSupported<Rlock>();

    public Task<Rgetlock> GetlockAsync(Tgetlock tgetlock) => NotSupported<Rgetlock>();

    public Task<Rlink> LinkAsync(Tlink tlink) => NotSupported<Rlink>();

    public Task<Rmkdir> MkdirAsync(Tmkdir tmkdir) => NotSupported<Rmkdir>();

    public Task<Rrenameat> RenameatAsync(Trenameat trenameat) => NotSupported<Rrenameat>();

    public Task<Runlinkat> UnlinkatAsync(Tunlinkat tunlinkat) => NotSupported<Runlinkat>();

    public INinePFileSystem Clone() => new MarkerFileSystem(_marker) { Dialect = Dialect };

    private static Task<T> NotSupported<T>()
    {
        return Task.FromException<T>(new NinePNotSupportedException("Operation is not supported by MarkerFileSystem"));
    }
}

internal sealed class CreateTrackingFileSystem : INinePFileSystem
{
    private readonly string _marker;
    private readonly List<string> _created = new();

    internal CreateTrackingFileSystem(string marker)
    {
        _marker = marker;
    }

    public bool Dialect { get; set; }

    public Task<Rwalk> WalkAsync(Twalk twalk)
    {
        var qids = twalk.Wname.Select((_, i) => new Qid(QidType.QTFILE, 0, (ulong)(_marker.GetHashCode() + i + 1))).ToArray();
        return Task.FromResult(new Rwalk(twalk.Tag, qids));
    }

    public Task<Ropen> OpenAsync(Topen topen)
        => Task.FromResult(new Ropen(topen.Tag, new Qid(QidType.QTFILE, 0, (ulong)_marker.GetHashCode()), 0));

    public Task<Rread> ReadAsync(Tread tread)
    {
        var payload = _created.Count == 0 ? _marker : string.Join(",", _created);
        return Task.FromResult(new Rread(tread.Tag, Encoding.UTF8.GetBytes(payload)));
    }

    public Task<Rcreate> CreateAsync(Tcreate tcreate)
    {
        _created.Add(tcreate.Name);
        ulong path = (ulong)Math.Abs((_marker + ":" + tcreate.Name).GetHashCode());
        return Task.FromResult(new Rcreate(tcreate.Tag, new Qid(QidType.QTFILE, 0, path), 8192));
    }

    public Task<Rwrite> WriteAsync(Twrite twrite) => Task.FromResult(new Rwrite(twrite.Tag, (uint)twrite.Data.Length));
    public Task<Rclunk> ClunkAsync(Tclunk tclunk) => Task.FromResult(new Rclunk(tclunk.Tag));
    public Task<Rstat> StatAsync(Tstat tstat) => NotSupported<Rstat>();
    public Task<Rwstat> WstatAsync(Twstat twstat) => NotSupported<Rwstat>();
    public Task<Rremove> RemoveAsync(Tremove tremove) => Task.FromResult(new Rremove(tremove.Tag));
    public Task<Rreaddir> ReaddirAsync(Treaddir treaddir) => NotSupported<Rreaddir>();
    public INinePFileSystem Clone() => new CreateTrackingFileSystem(_marker);

    private static Task<T> NotSupported<T>() => Task.FromException<T>(new NinePNotSupportedException());
}

internal sealed class DirectoryListingFileSystem : INinePFileSystem
{
    private readonly string[] _entries;

    internal DirectoryListingFileSystem(IEnumerable<string> entries)
    {
        _entries = entries.ToArray();
    }

    public bool Dialect { get; set; }

    public Task<Rwalk> WalkAsync(Twalk twalk)
    {
        var qids = twalk.Wname.Select((name, i) => new Qid(QidType.QTDIR, 0, (ulong)Math.Abs((name + i).GetHashCode()))).ToArray();
        return Task.FromResult(new Rwalk(twalk.Tag, qids));
    }

    public Task<Ropen> OpenAsync(Topen topen)
        => Task.FromResult(new Ropen(topen.Tag, new Qid(QidType.QTDIR, 0, 1), 8192));

    public Task<Rread> ReadAsync(Tread tread)
        => Task.FromResult(new Rread(tread.Tag, Array.Empty<byte>()));

    public Task<Rreaddir> ReaddirAsync(Treaddir treaddir)
    {
        var bytes = new List<byte>();
        ulong nextOffset = 0;

        foreach (var name in _entries)
        {
            nextOffset++;
            if (nextOffset <= treaddir.Offset)
            {
                continue;
            }

            var qid = new Qid(QidType.QTDIR, 0, (ulong)Math.Abs(name.GetHashCode()));
            int nameLen = Encoding.UTF8.GetByteCount(name);
            int entrySize = 13 + 8 + 1 + 2 + nameLen;
            byte[] entry = new byte[entrySize];
            int offset = 0;
            entry[offset++] = (byte)qid.Type;
            BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(offset, 4), qid.Version);
            offset += 4;
            BinaryPrimitives.WriteUInt64LittleEndian(entry.AsSpan(offset, 8), qid.Path);
            offset += 8;
            BinaryPrimitives.WriteUInt64LittleEndian(entry.AsSpan(offset, 8), nextOffset);
            offset += 8;
            entry[offset++] = (byte)qid.Type;
            BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(offset, 2), (ushort)nameLen);
            offset += 2;
            Encoding.UTF8.GetBytes(name).CopyTo(entry.AsSpan(offset, nameLen));

            if (bytes.Count + entry.Length > treaddir.Count)
            {
                break;
            }

            bytes.AddRange(entry);
        }

        byte[] data = bytes.ToArray();
        return Task.FromResult(new Rreaddir((uint)(data.Length + NinePConstants.HeaderSize + 4), treaddir.Tag, (uint)data.Length, data));
    }

    public Task<Rwrite> WriteAsync(Twrite twrite) => Task.FromResult(new Rwrite(twrite.Tag, (uint)twrite.Data.Length));
    public Task<Rclunk> ClunkAsync(Tclunk tclunk) => Task.FromResult(new Rclunk(tclunk.Tag));
    public Task<Rstat> StatAsync(Tstat tstat) => NotSupported<Rstat>();
    public Task<Rwstat> WstatAsync(Twstat twstat) => NotSupported<Rwstat>();
    public Task<Rremove> RemoveAsync(Tremove tremove) => Task.FromResult(new Rremove(tremove.Tag));
    public Task<Rcreate> CreateAsync(Tcreate tcreate) => NotSupported<Rcreate>();
    public INinePFileSystem Clone() => new DirectoryListingFileSystem(_entries) { Dialect = Dialect };

    private static Task<T> NotSupported<T>() => Task.FromException<T>(new NinePNotSupportedException());
}

internal sealed class ExistingPathFileSystem : INinePFileSystem
{
    private readonly HashSet<string> _paths;
    private List<string> _currentPath = new();

    internal ExistingPathFileSystem(IEnumerable<string> paths)
    {
        _paths = new HashSet<string>(paths.Select(NormalizePath), StringComparer.Ordinal) { "/" };
    }

    private ExistingPathFileSystem(HashSet<string> paths, List<string> currentPath)
    {
        _paths = paths;
        _currentPath = currentPath;
    }

    public bool Dialect { get; set; }

    public Task<Rwalk> WalkAsync(Twalk twalk)
    {
        var temp = new List<string>(_currentPath);
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

        _currentPath = temp;
        return Task.FromResult(new Rwalk(twalk.Tag, qids.ToArray()));
    }

    public Task<Ropen> OpenAsync(Topen topen)
        => Task.FromResult(new Ropen(topen.Tag, new Qid(QidType.QTFILE, 0, 1), 8192));

    public Task<Rread> ReadAsync(Tread tread)
        => Task.FromResult(new Rread(tread.Tag, Encoding.UTF8.GetBytes(Normalize(_currentPath))));

    public Task<Rwrite> WriteAsync(Twrite twrite) => Task.FromResult(new Rwrite(twrite.Tag, (uint)twrite.Data.Length));
    public Task<Rclunk> ClunkAsync(Tclunk tclunk) => Task.FromResult(new Rclunk(tclunk.Tag));
    public Task<Rstat> StatAsync(Tstat tstat) => NotSupported<Rstat>();
    public Task<Rwstat> WstatAsync(Twstat twstat) => NotSupported<Rwstat>();
    public Task<Rremove> RemoveAsync(Tremove tremove) => Task.FromResult(new Rremove(tremove.Tag));
    public Task<Rcreate> CreateAsync(Tcreate tcreate) => NotSupported<Rcreate>();
    public Task<Rreaddir> ReaddirAsync(Treaddir treaddir) => NotSupported<Rreaddir>();
    public INinePFileSystem Clone() => new ExistingPathFileSystem(_paths, new List<string>(_currentPath)) { Dialect = Dialect };

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

    private static Task<T> NotSupported<T>() => Task.FromException<T>(new NinePNotSupportedException());
}

internal sealed class SharedMutableFileSystem : INinePFileSystem
{
    private sealed class SharedState
    {
        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal)
        {
            ["/"] = Array.Empty<byte>()
        };
    }

    private readonly SharedState _state;
    private List<string> _currentPath = new();

    internal SharedMutableFileSystem()
        : this(new SharedState())
    {
    }

    private SharedMutableFileSystem(SharedState state)
    {
        _state = state;
    }

    public bool Dialect { get; set; }

    private string GetFullPath() => _currentPath.Count == 0 ? "/" : "/" + string.Join("/", _currentPath);

    public Task<Rwalk> WalkAsync(Twalk twalk)
    {
        var tempPath = new List<string>(_currentPath);
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

        _currentPath = tempPath;
        return Task.FromResult(new Rwalk(twalk.Tag, qids.ToArray()));
    }

    public Task<Ropen> OpenAsync(Topen topen)
    {
        string path = GetFullPath();
        var qidType = _state.Files.ContainsKey(path) ? QidType.QTFILE : QidType.QTDIR;
        return Task.FromResult(new Ropen(topen.Tag, new Qid(qidType, 0, (ulong)Math.Abs(path.GetHashCode())), 8192));
    }

    public Task<Rread> ReadAsync(Tread tread)
    {
        string path = GetFullPath();
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

    public Task<Rwrite> WriteAsync(Twrite twrite)
    {
        string path = GetFullPath();
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

    public Task<Rcreate> CreateAsync(Tcreate tcreate)
    {
        string parent = GetFullPath();
        string path = parent == "/" ? "/" + tcreate.Name : parent + "/" + tcreate.Name;
        _state.Files[path] = Array.Empty<byte>();
        _currentPath.Add(tcreate.Name);
        return Task.FromResult(new Rcreate(tcreate.Tag, new Qid(QidType.QTFILE, 0, (ulong)Math.Abs(path.GetHashCode())), 8192));
    }

    public Task<Rclunk> ClunkAsync(Tclunk tclunk) => Task.FromResult(new Rclunk(tclunk.Tag));
    public Task<Rstat> StatAsync(Tstat tstat) => NotSupported<Rstat>();
    public Task<Rwstat> WstatAsync(Twstat twstat) => NotSupported<Rwstat>();
    public Task<Rremove> RemoveAsync(Tremove tremove) => Task.FromResult(new Rremove(tremove.Tag));
    public Task<Rreaddir> ReaddirAsync(Treaddir treaddir) => NotSupported<Rreaddir>();

    public INinePFileSystem Clone()
    {
        var clone = new SharedMutableFileSystem(_state)
        {
            Dialect = Dialect,
            _currentPath = new List<string>(_currentPath)
        };

        return clone;
    }

    private static Task<T> NotSupported<T>() => Task.FromException<T>(new NinePNotSupportedException());
}
