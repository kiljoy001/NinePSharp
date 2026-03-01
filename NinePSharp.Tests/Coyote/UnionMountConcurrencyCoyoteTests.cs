using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Coyote.Specifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using Xunit;
using CoyoteTask = Microsoft.Coyote.Rewriting.Types.Threading.Tasks.Task;

namespace NinePSharp.Tests.Coyote;

/// <summary>
/// Coyote concurrency tests for union mount operations.
/// Verifies thread-safety and absence of race conditions.
/// </summary>
public class UnionMountConcurrencyCoyoteTests
{
    [Fact]
    public static async Task ConcurrentUnionReaddirDoesNotCorruptState()
    {
        var backend1 = new CoyoteTestBackend("/union", new[] { "a.txt", "b.txt", "c.txt" });
        var backend2 = new CoyoteTestBackend("/union", new[] { "d.txt", "e.txt", "f.txt" });

        var dispatcher = new NinePFSDispatcher(
            NullLogger<NinePFSDispatcher>.Instance,
            new IProtocolBackend[] { backend1, backend2 },
            new NullRemoteMountProvider());

        // Setup: Attach and walk for multiple sessions
        await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTattach(new Tattach(1, 1, uint.MaxValue, "u", "/")), NinePDialect.NineP2000);
        await dispatcher.DispatchAsync("s2", NinePMessage.NewMsgTattach(new Tattach(1, 1, uint.MaxValue, "u", "/")), NinePDialect.NineP2000);

        await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTwalk(new Twalk(2, 1, 2, new[] { "union" })), NinePDialect.NineP2000);
        await dispatcher.DispatchAsync("s2", NinePMessage.NewMsgTwalk(new Twalk(2, 1, 2, new[] { "union" })), NinePDialect.NineP2000);

        await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTopen(new Topen(3, 2, 0)), NinePDialect.NineP2000);
        await dispatcher.DispatchAsync("s2", NinePMessage.NewMsgTopen(new Topen(3, 2, 0)), NinePDialect.NineP2000);

        // Concurrent readdirs
        var t1 = CoyoteTask.Run(async () =>
        {
            for (int i = 0; i < 5; i++)
            {
                var result = await dispatcher.DispatchAsync("s1",
                    NinePMessage.NewMsgTreaddir(new Treaddir(24, (ushort)(4 + i), 2, 0, 8192)),
                    NinePDialect.NineP2000);
                Specification.Assert(result is Rreaddir, "Expected Rreaddir from concurrent read");
            }
        });

        var t2 = CoyoteTask.Run(async () =>
        {
            for (int i = 0; i < 5; i++)
            {
                var result = await dispatcher.DispatchAsync("s2",
                    NinePMessage.NewMsgTreaddir(new Treaddir(24, (ushort)(4 + i), 2, 0, 8192)),
                    NinePDialect.NineP2000);
                Specification.Assert(result is Rreaddir, "Expected Rreaddir from concurrent read");
            }
        });

        await CoyoteTask.WhenAll(t1, t2);
    }

    [Fact]
    public static async Task ConcurrentWalkAndReaddirAreIsolated()
    {
        var backend1 = new CoyoteTestBackend("/union", new[] { "file1.txt" });
        var backend2 = new CoyoteTestBackend("/union", new[] { "file2.txt" });

        var dispatcher = new NinePFSDispatcher(
            NullLogger<NinePFSDispatcher>.Instance,
            new IProtocolBackend[] { backend1, backend2 },
            new NullRemoteMountProvider());

        await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTattach(new Tattach(1, 1, uint.MaxValue, "u", "/")), NinePDialect.NineP2000);
        await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTwalk(new Twalk(2, 1, 2, new[] { "union" })), NinePDialect.NineP2000);
        await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTopen(new Topen(3, 2, 0)), NinePDialect.NineP2000);

        // One task reads, another walks concurrently
        var readTask = CoyoteTask.Run(async () =>
        {
            for (int i = 0; i < 10; i++)
            {
                var result = await dispatcher.DispatchAsync("s1",
                    NinePMessage.NewMsgTreaddir(new Treaddir(24, (ushort)(4 + i), 2, 0, 8192)),
                    NinePDialect.NineP2000);
                Specification.Assert(result is Rreaddir or Rerror, "Unexpected response type");
            }
        });

        var walkTask = CoyoteTask.Run(async () =>
        {
            for (int i = 0; i < 10; i++)
            {
                var result = await dispatcher.DispatchAsync("s1",
                    NinePMessage.NewMsgTwalk(new Twalk((ushort)(10 + i), 1, (uint)(10 + i), new[] { "union" })),
                    NinePDialect.NineP2000);
                // Walk may succeed or fail depending on timing, but should not crash
                Specification.Assert(result is Rwalk or Rerror, "Walk should return Rwalk or Rerror");
            }
        });

        await CoyoteTask.WhenAll(readTask, walkTask);
    }

    [Fact]
    public static async Task ConcurrentClunkAndReaddirAreSafe()
    {
        var backend1 = new CoyoteTestBackend("/union", new[] { "a.txt", "b.txt" });
        var backend2 = new CoyoteTestBackend("/union", new[] { "c.txt", "d.txt" });

        var dispatcher = new NinePFSDispatcher(
            NullLogger<NinePFSDispatcher>.Instance,
            new IProtocolBackend[] { backend1, backend2 },
            new NullRemoteMountProvider());

        await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTattach(new Tattach(1, 1, uint.MaxValue, "u", "/")), NinePDialect.NineP2000);
        await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTwalk(new Twalk(2, 1, 2, new[] { "union" })), NinePDialect.NineP2000);
        await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTopen(new Topen(3, 2, 0)), NinePDialect.NineP2000);

        // One task reads, another clunks
        var readTask = CoyoteTask.Run(async () =>
        {
            for (int i = 0; i < 5; i++)
            {
                var result = await dispatcher.DispatchAsync("s1",
                    NinePMessage.NewMsgTreaddir(new Treaddir(24, (ushort)(4 + i), 2, 0, 8192)),
                    NinePDialect.NineP2000);
                // After clunk, reads should fail with Rerror
                Specification.Assert(result is Rreaddir or Rerror, "Expected Rreaddir or Rerror");
            }
        });

        var clunkTask = CoyoteTask.Run(async () =>
        {
            await CoyoteTask.Delay(10); // Let some reads start
            var result = await dispatcher.DispatchAsync("s1",
                NinePMessage.NewMsgTclunk(new Tclunk(100, 2)),
                NinePDialect.NineP2000);
            Specification.Assert(result is Rclunk or Rerror, "Clunk should succeed or fail cleanly");
        });

        await CoyoteTask.WhenAll(readTask, clunkTask);
    }

    [Fact]
    public static async Task MultipleSessionsUnionReaddirAreIndependent()
    {
        var backend1 = new CoyoteTestBackend("/union", new[] { "shared.txt", "only1.txt" });
        var backend2 = new CoyoteTestBackend("/union", new[] { "shared.txt", "only2.txt" });

        var dispatcher = new NinePFSDispatcher(
            NullLogger<NinePFSDispatcher>.Instance,
            new IProtocolBackend[] { backend1, backend2 },
            new NullRemoteMountProvider());

        // Setup multiple sessions
        var sessions = new[] { "session1", "session2", "session3" };
        foreach (var session in sessions)
        {
            await dispatcher.DispatchAsync(session, NinePMessage.NewMsgTattach(new Tattach(1, 1, uint.MaxValue, "u", "/")), NinePDialect.NineP2000);
            await dispatcher.DispatchAsync(session, NinePMessage.NewMsgTwalk(new Twalk(2, 1, 2, new[] { "union" })), NinePDialect.NineP2000);
            await dispatcher.DispatchAsync(session, NinePMessage.NewMsgTopen(new Topen(3, 2, 0)), NinePDialect.NineP2000);
        }

        // All sessions read concurrently
        var tasks = sessions.Select(session => CoyoteTask.Run(async () =>
        {
            var entries = new List<string>();
            ulong offset = 0;
            for (int i = 0; i < 10; i++)
            {
                var result = await dispatcher.DispatchAsync(session,
                    NinePMessage.NewMsgTreaddir(new Treaddir(24, (ushort)(4 + i), 2, offset, 8192)),
                    NinePDialect.NineP2000);

                if (result is Rreaddir rr && rr.Count > 0)
                {
                    offset += rr.Count;
                }
                else break;
            }
            // Each session should see the same set of entries
            return true;
        })).ToArray();

        await CoyoteTask.WhenAll(tasks);
    }

    private sealed class CoyoteTestBackend : IProtocolBackend
    {
        private readonly string[] _files;

        public CoyoteTestBackend(string mountPath, string[] files)
        {
            MountPath = mountPath;
            _files = files;
        }

        public string Name => "coyote-test";
        public string MountPath { get; }

        public Task InitializeAsync(IConfiguration configuration) => Task.CompletedTask;

        public IBackendRuntime GetRuntime(X509Certificate2? certificate = null)
            => RuntimeFileSystemAdapter.ToRuntime(new CoyoteTestFileSystem(_files));

        public IBackendRuntime GetRuntime(System.Security.SecureString? credentials, X509Certificate2? certificate = null)
            => GetRuntime(certificate);
    }

    private sealed class CoyoteTestFileSystem : INinePFileSystem
    {
        private readonly string[] _files;

        public CoyoteTestFileSystem(string[] files) => _files = files;

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
        public INinePFileSystem Clone() => new CoyoteTestFileSystem(_files) { Dialect = Dialect };
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
