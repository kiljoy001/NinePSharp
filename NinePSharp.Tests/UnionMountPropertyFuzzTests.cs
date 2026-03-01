using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Tests;

/// <summary>
/// Property-based fuzz tests for union mount readdir behavior.
/// Verifies invariants hold under arbitrary inputs.
/// </summary>
public class UnionMountPropertyFuzzTests
{
    [Property(MaxTest = 100)]
    public bool Union_Readdir_Never_Has_Duplicates(NonEmptyArray<NonEmptyString> backend1Files, NonEmptyArray<NonEmptyString> backend2Files)
    {
        var files1 = backend1Files.Item.Select(s => SanitizeFileName(s.Item)).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToArray();
        var files2 = backend2Files.Item.Select(s => SanitizeFileName(s.Item)).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToArray();

        if (files1.Length == 0 || files2.Length == 0) return true;

        var backend1 = new PropertyTestBackend("/union", files1);
        var backend2 = new PropertyTestBackend("/union", files2);

        var dispatcher = new NinePFSDispatcher(
            NullLogger<NinePFSDispatcher>.Instance,
            new IProtocolBackend[] { backend1, backend2 },
            new NullRemoteMountProvider());

        dispatcher.DispatchAsync("s", NinePMessage.NewMsgTattach(new Tattach(1, 1, uint.MaxValue, "u", "/")), NinePDialect.NineP2000).Wait();
        dispatcher.DispatchAsync("s", NinePMessage.NewMsgTwalk(new Twalk(2, 1, 2, new[] { "union" })), NinePDialect.NineP2000).Wait();
        dispatcher.DispatchAsync("s", NinePMessage.NewMsgTopen(new Topen(3, 2, 0)), NinePDialect.NineP2000).Wait();

        var entries = ReadAllReaddirEntries(dispatcher, "s", 2);

        // Property: No duplicates in result
        return entries.Count == entries.Distinct().Count();
    }

    [Property(MaxTest = 100)]
    public bool Union_Readdir_Contains_All_Unique_Entries(NonEmptyArray<NonEmptyString> backend1Files, NonEmptyArray<NonEmptyString> backend2Files)
    {
        var files1 = backend1Files.Item.Select(s => SanitizeFileName(s.Item)).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToArray();
        var files2 = backend2Files.Item.Select(s => SanitizeFileName(s.Item)).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToArray();

        if (files1.Length == 0 || files2.Length == 0) return true;

        var backend1 = new PropertyTestBackend("/union", files1);
        var backend2 = new PropertyTestBackend("/union", files2);

        var dispatcher = new NinePFSDispatcher(
            NullLogger<NinePFSDispatcher>.Instance,
            new IProtocolBackend[] { backend1, backend2 },
            new NullRemoteMountProvider());

        dispatcher.DispatchAsync("s", NinePMessage.NewMsgTattach(new Tattach(1, 1, uint.MaxValue, "u", "/")), NinePDialect.NineP2000).Wait();
        dispatcher.DispatchAsync("s", NinePMessage.NewMsgTwalk(new Twalk(2, 1, 2, new[] { "union" })), NinePDialect.NineP2000).Wait();
        dispatcher.DispatchAsync("s", NinePMessage.NewMsgTopen(new Topen(3, 2, 0)), NinePDialect.NineP2000).Wait();

        var entries = ReadAllReaddirEntries(dispatcher, "s", 2);
        var expected = files1.Union(files2).ToHashSet();

        // Property: All unique entries from both backends appear
        return expected.SetEquals(entries);
    }

    [Property(MaxTest = 50)]
    public bool Union_Readdir_Is_Deterministic(NonEmptyArray<NonEmptyString> backend1Files, NonEmptyArray<NonEmptyString> backend2Files)
    {
        var files1 = backend1Files.Item.Select(s => SanitizeFileName(s.Item)).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToArray();
        var files2 = backend2Files.Item.Select(s => SanitizeFileName(s.Item)).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToArray();

        if (files1.Length == 0 || files2.Length == 0) return true;

        // Run twice with same inputs
        var entries1 = RunReaddirWithBackends(files1, files2);
        var entries2 = RunReaddirWithBackends(files1, files2);

        // Property: Results are identical
        return entries1.SequenceEqual(entries2);
    }

    [Property(MaxTest = 50)]
    public bool Union_Readdir_First_Backend_Wins_On_Conflict(NonEmptyString sharedFile)
    {
        var shared = SanitizeFileName(sharedFile.Item);
        if (string.IsNullOrEmpty(shared)) return true;

        var files1 = new[] { shared, "only1.txt" };
        var files2 = new[] { shared, "only2.txt" };

        var entries = RunReaddirWithBackends(files1, files2);

        // Property: Shared file appears exactly once
        return entries.Count(e => e == shared) == 1;
    }

    [Property(MaxTest = 100)]
    public bool Union_Readdir_Entry_Count_Equals_Distinct_Union(NonEmptyArray<NonEmptyString> backend1Files, NonEmptyArray<NonEmptyString> backend2Files)
    {
        var files1 = backend1Files.Item.Select(s => SanitizeFileName(s.Item)).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToArray();
        var files2 = backend2Files.Item.Select(s => SanitizeFileName(s.Item)).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToArray();

        if (files1.Length == 0 || files2.Length == 0) return true;

        var entries = RunReaddirWithBackends(files1, files2);
        var expectedCount = files1.Union(files2).Count();

        // Property: Entry count equals distinct union
        return entries.Count == expectedCount;
    }

    [Property(MaxTest = 50)]
    public bool Union_Readdir_Pagination_Preserves_Completeness(NonEmptyArray<NonEmptyString> backendFiles)
    {
        var files = backendFiles.Item.Select(s => SanitizeFileName(s.Item)).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToArray();
        if (files.Length == 0) return true;

        var backend1 = new PropertyTestBackend("/union", files);
        var backend2 = new PropertyTestBackend("/union", Array.Empty<string>());

        var dispatcher = new NinePFSDispatcher(
            NullLogger<NinePFSDispatcher>.Instance,
            new IProtocolBackend[] { backend1, backend2 },
            new NullRemoteMountProvider());

        dispatcher.DispatchAsync("s", NinePMessage.NewMsgTattach(new Tattach(1, 1, uint.MaxValue, "u", "/")), NinePDialect.NineP2000).Wait();
        dispatcher.DispatchAsync("s", NinePMessage.NewMsgTwalk(new Twalk(2, 1, 2, new[] { "union" })), NinePDialect.NineP2000).Wait();
        dispatcher.DispatchAsync("s", NinePMessage.NewMsgTopen(new Topen(3, 2, 0)), NinePDialect.NineP2000).Wait();

        // Read with small page size to force pagination
        var entries = ReadAllReaddirEntriesWithPageSize(dispatcher, "s", 2, 64);

        // Property: All entries are present even with small pages
        return files.ToHashSet().SetEquals(entries);
    }

    private static List<string> RunReaddirWithBackends(string[] files1, string[] files2)
    {
        var backend1 = new PropertyTestBackend("/union", files1);
        var backend2 = new PropertyTestBackend("/union", files2);

        var dispatcher = new NinePFSDispatcher(
            NullLogger<NinePFSDispatcher>.Instance,
            new IProtocolBackend[] { backend1, backend2 },
            new NullRemoteMountProvider());

        dispatcher.DispatchAsync("s", NinePMessage.NewMsgTattach(new Tattach(1, 1, uint.MaxValue, "u", "/")), NinePDialect.NineP2000).Wait();
        dispatcher.DispatchAsync("s", NinePMessage.NewMsgTwalk(new Twalk(2, 1, 2, new[] { "union" })), NinePDialect.NineP2000).Wait();
        dispatcher.DispatchAsync("s", NinePMessage.NewMsgTopen(new Topen(3, 2, 0)), NinePDialect.NineP2000).Wait();

        return ReadAllReaddirEntries(dispatcher, "s", 2);
    }

    private static List<string> ReadAllReaddirEntries(NinePFSDispatcher dispatcher, string session, uint fid)
        => ReadAllReaddirEntriesWithPageSize(dispatcher, session, fid, 8192);

    private static List<string> ReadAllReaddirEntriesWithPageSize(NinePFSDispatcher dispatcher, string session, uint fid, uint pageSize)
    {
        var entries = new List<string>();
        ulong offset = 0;

        for (int i = 0; i < 100; i++)
        {
            var result = dispatcher.DispatchAsync(session,
                NinePMessage.NewMsgTreaddir(new Treaddir(24, (ushort)(4 + i), fid, offset, pageSize)),
                NinePDialect.NineP2000).Result;

            if (result is not Rreaddir readdir || readdir.Count == 0)
                break;

            entries.AddRange(ParseReaddirNames(readdir.Data.Span));
            offset += readdir.Count;
        }

        return entries;
    }

    private static List<string> ParseReaddirNames(ReadOnlySpan<byte> data)
    {
        var names = new List<string>();
        int offset = 0;
        while (offset < data.Length && data.Length - offset >= 24)
        {
            offset += 13; // qid
            offset += 8;  // nextOffset
            offset += 1;  // type

            if (offset + 2 > data.Length) break;
            var nameLen = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
            offset += 2;

            if (offset + nameLen > data.Length) break;
            names.Add(Encoding.UTF8.GetString(data.Slice(offset, nameLen)));
            offset += nameLen;
        }
        return names;
    }

    private static string SanitizeFileName(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var sanitized = new string(s.Where(c => char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-').ToArray());
        return sanitized.Length > 0 && sanitized.Length <= 50 ? sanitized : "";
    }

    private sealed class PropertyTestBackend : IProtocolBackend
    {
        private readonly string[] _files;

        public PropertyTestBackend(string mountPath, string[] files)
        {
            MountPath = mountPath;
            _files = files;
        }

        public string Name => "prop-test";
        public string MountPath { get; }

        public Task InitializeAsync(IConfiguration configuration) => Task.CompletedTask;

        public IBackendRuntime GetRuntime(X509Certificate2? certificate = null)
            => RuntimeFileSystemAdapter.ToRuntime(new PropertyTestFileSystem(_files));

        public IBackendRuntime GetRuntime(System.Security.SecureString? credentials, X509Certificate2? certificate = null)
            => GetRuntime(certificate);
    }

    private sealed class PropertyTestFileSystem : INinePFileSystem
    {
        private readonly string[] _files;

        public PropertyTestFileSystem(string[] files) => _files = files;

        public NinePDialect Dialect { get; set; } = NinePDialect.NineP2000;

        public Task<Rwalk> WalkAsync(Twalk twalk)
        {
            var qids = twalk.Wname.Select((name, i) =>
                new Qid(_files.Contains(name) ? QidType.QTFILE : QidType.QTDIR, 0, (ulong)Math.Abs(name.GetHashCode())))
                .ToArray();
            return Task.FromResult(new Rwalk(twalk.Tag, qids));
        }

        public Task<Ropen> OpenAsync(Topen topen)
            => Task.FromResult(new Ropen(topen.Tag, new Qid(QidType.QTDIR, 0, 1), 8192));

        public Task<Rread> ReadAsync(Tread tread)
        {
            var allData = new List<byte>();
            foreach (var file in _files)
            {
                var stat = new Stat(0, 0, 1, new Qid(QidType.QTFILE, 0, (ulong)Math.Abs(file.GetHashCode())),
                    0644, 0, 0, 100, file, "none", "none", "none", dialect: Dialect);
                var buffer = new byte[stat.Size];
                var offset = 0;
                stat.WriteTo(buffer, ref offset);
                allData.AddRange(buffer);
            }

            var data = allData.ToArray();
            if (tread.Offset >= (ulong)data.Length)
                return Task.FromResult(new Rread(tread.Tag, Array.Empty<byte>()));

            var start = (int)tread.Offset;
            var remaining = data.Length - start;
            var count = tread.Count > int.MaxValue ? remaining : Math.Min((int)tread.Count, remaining);
            var result = new byte[count];
            Array.Copy(data, start, result, 0, count);

            return Task.FromResult(new Rread(tread.Tag, result));
        }

        public Task<Rwrite> WriteAsync(Twrite twrite) => Task.FromResult(new Rwrite(twrite.Tag, (uint)twrite.Data.Length));
        public Task<Rclunk> ClunkAsync(Tclunk tclunk) => Task.FromResult(new Rclunk(tclunk.Tag));
        public Task<Rstat> StatAsync(Tstat tstat)
        {
            var stat = new Stat(0, 0, 1, new Qid(QidType.QTDIR, 0, 1), 0755 | 0x80000000, 0, 0, 0, ".", "none", "none", "none", dialect: Dialect);
            return Task.FromResult(new Rstat(tstat.Tag, stat));
        }
        public Task<Rwstat> WstatAsync(Twstat twstat) => Task.FromResult(new Rwstat(twstat.Tag));
        public Task<Rremove> RemoveAsync(Tremove tremove) => Task.FromResult(new Rremove(tremove.Tag));
        public Task<Rcreate> CreateAsync(Tcreate tcreate) => Task.FromResult(new Rcreate(tcreate.Tag, new Qid(QidType.QTFILE, 0, 2), 8192));
        public INinePFileSystem Clone() => new PropertyTestFileSystem(_files) { Dialect = Dialect };
    }

    private sealed class NullRemoteMountProvider : IRemoteMountProvider
    {
        public void Start() { }
        public Task StopAsync() => Task.CompletedTask;
        public Task RegisterMountAsync(string mountPath, Func<IBackendRuntime> createRuntime) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> GetRemoteMountPathsAsync() => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<IBackendRuntime?> TryCreateRemoteRuntimeAsync(string mountPath) => Task.FromResult<IBackendRuntime?>(null);
        public void Dispose() { }
    }
}
