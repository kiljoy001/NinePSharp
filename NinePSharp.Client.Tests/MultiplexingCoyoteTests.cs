using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Coyote.SystematicTesting;
using NinePSharp.Client;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Server.FSharp;
using NinePSharp.Server.Interfaces;
using NinePSharp.Parser;
using NinePSharp.Interfaces;
using NinePSharp.Server;
using Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace NinePSharp.Client.Tests;

public class MultiplexingCoyoteTests
{
    private class SyncBackend : INinePRequestHandler
    {
        public Task<IAuthHandler?> GetAuthHandlerAsync(Tauth msg, CancellationToken ct) => Task.FromResult<IAuthHandler?>(null);
        public Task<Rattach> AttachAsync(Tattach msg, CancellationToken ct) => Task.FromResult(new Rattach(msg.Tag, new Qid(QidType.QTDIR, 0, 0)));
        public Task<Rwalk> WalkAsync(string[] relativePath, Twalk msg, CancellationToken ct) => Task.FromResult(new Rwalk(msg.Tag, Array.Empty<Qid>()));
        public Task<Ropen> OpenAsync(string[] relativePath, Topen msg, CancellationToken ct) => Task.FromResult(new Ropen(msg.Tag, new Qid(QidType.QTFILE, 0, 0), 0));
        public Task<Rread> ReadAsync(string[] relativePath, Tread msg, CancellationToken ct) => Task.FromResult(new Rread(msg.Tag, Array.Empty<byte>()));
        public Task<Rwrite> WriteAsync(string[] relativePath, Twrite msg, CancellationToken ct) => Task.FromResult(new Rwrite(msg.Tag, 0u));
        public Task<Rclunk> ClunkAsync(string[] relativePath, Tclunk msg, CancellationToken ct) => Task.FromResult(new Rclunk(msg.Tag));
        public Task<Rstat> StatAsync(string[] relativePath, Tstat msg, CancellationToken ct) => Task.FromResult(new Rstat(msg.Tag, new Stat(0,0,0, new Qid(QidType.QTFILE, 0, 0), 0, 0, 0, 0, "", "", "", "", NinePDialect.NineP2000L)));
        public Task<Rwstat> WstatAsync(string[] relativePath, Twstat msg, CancellationToken ct) => Task.FromResult(new Rwstat(msg.Tag));
        public Task<Rcreate> CreateAsync(string[] parentPath, Tcreate msg, CancellationToken ct) => Task.FromResult(new Rcreate(msg.Tag, new Qid(QidType.QTFILE, 0, 0), 0));
        public Task<Rremove> RemoveAsync(string[] relativePath, Tremove msg, CancellationToken ct) => Task.FromResult(new Rremove(msg.Tag));
        public Task<Rreaddir>? ReaddirAsync(string[] relativePath, Treaddir msg, CancellationToken ct) => Task.FromResult(new Rreaddir((uint)NinePConstants.HeaderSize + 4, msg.Tag, 0u, ReadOnlyMemory<byte>.Empty));
        public Task<Rsymlink> SymlinkAsync(string[] relativePath, Tsymlink msg, CancellationToken ct) => Task.FromResult(new Rsymlink((uint)NinePConstants.HeaderSize + 13, msg.Tag, new Qid(QidType.QTFILE, 0, 0)));
        public Task<Rreadlink> ReadlinkAsync(string[] relativePath, Treadlink msg, CancellationToken ct) => Task.FromResult(new Rreadlink((uint)NinePConstants.HeaderSize + 2, msg.Tag, ""));
        public Task<Rlink> LinkAsync(string[] relativePath, Tlink msg, CancellationToken ct) => Task.FromResult(new Rlink((uint)NinePConstants.HeaderSize, msg.Tag));
        public Task<Rlerror> LockAsync(string[] relativePath, Tlock msg, CancellationToken ct) => Task.FromResult(new Rlerror(msg.Tag, 0u));
        public Task<Rgetlock> GetlockAsync(string[] relativePath, Tgetlock msg, CancellationToken ct) => Task.FromResult(new Rgetlock((uint)NinePConstants.HeaderSize + 23, msg.Tag, (byte)0, 0UL, 0UL, 0u, ""));
        public Task<Rxattrwalk> XattrwalkAsync(string[] relativePath, Txattrwalk msg, CancellationToken ct) => Task.FromResult(new Rxattrwalk((uint)NinePConstants.HeaderSize + 8, msg.Tag, 0u));
        public Task<Rxattrcreate> XattrcreateAsync(string[] relativePath, Txattrcreate msg, CancellationToken ct) => Task.FromResult(new Rxattrcreate((uint)NinePConstants.HeaderSize, msg.Tag));
        public Task<Rflush> FlushAsync(Tflush msg, CancellationToken ct) => Task.FromResult(new Rflush(msg.Tag));
    }

    [Fact]
    public void Coyote_Dispatcher_Concurrency_Test()
    {
        var configuration = Microsoft.Coyote.Configuration.Create()
            .WithTestingIterations(20)
            .WithMaxSchedulingSteps(200);

        var engine = TestingEngine.Create(configuration, async () =>
        {
            var handler = new SyncBackend();
            var dispatcher = new NinePFSDispatcherEngine(handler);
            var sessionId = "test-session";
            var dialect = NinePDialect.NineP2000L;
            var cert = new System.Security.Cryptography.X509Certificates.X509Certificate2();

            var tasks = Enumerable.Range(1, 3).Select(i => 
                ((INinePFSDispatcher)dispatcher).DispatchAsync(
                    sessionId, 
                    NinePMessage.NewMsgTversion(new Tversion((ushort)i, 8192u, "9P2000.L")),
                    dialect, 
                    cert)
            ).ToArray();

            await Task.WhenAll(tasks);
        });

        engine.Run();

        if (engine.TestReport.NumOfFoundBugs > 0)
        {
            Assert.Fail($"Coyote found a bug: {engine.TestReport.BugReports.First()}");
        }
    }
}
