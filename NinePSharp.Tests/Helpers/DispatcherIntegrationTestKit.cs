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
using NinePSharp.Server.Cluster;
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
            new Mock<IClusterManager>().Object);
    }

    internal static async Task AttachRootAsync(NinePFSDispatcher dispatcher, ushort tag, uint fid)
    {
        var response = await dispatcher.DispatchAsync(
            NinePMessage.NewMsgTattach(new Tattach(tag, fid, NinePConstants.NoFid, "user", "/")),
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
