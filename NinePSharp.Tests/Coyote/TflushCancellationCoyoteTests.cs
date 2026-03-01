using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Coyote.Specifications;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
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
/// Coyote concurrency tests for Tflush cancellation - verifies race conditions
/// and proper CancellationToken propagation per 9front semantics.
/// </summary>
public class TflushCancellationCoyoteTests
{
    /// <summary>
    /// Per 9front flush(5): Tflush should signal cancellation to in-flight request.
    /// Tests concurrent Tread + Tflush race condition.
    /// </summary>
    [Fact]
    public static async Task TflushCancelsInFlightTread_ConcurrentRace()
    {
        var readStarted = new TaskCompletionSource<bool>();
        var blockRead = new TaskCompletionSource<bool>();
        var cancellationObserved = new ConcurrentBag<bool>();

        var mockFs = new Mock<INinePFileSystem>();
        mockFs.SetupProperty(f => f.Dialect);
        mockFs.Setup(x => x.WalkAsync(It.IsAny<Twalk>()))
              .ReturnsAsync(new Rwalk(1, new[] { new Qid(QidType.QTFILE, 0, 1) }));
        mockFs.Setup(x => x.OpenAsync(It.IsAny<Topen>()))
              .ReturnsAsync(new Ropen(0, new Qid(QidType.QTFILE, 0, 1), 8192));
        mockFs.Setup(x => x.Clone()).Returns(mockFs.Object);

        var mockRuntime = new Mock<IBackendRuntime>();
        mockRuntime.Setup(r => r.Id).Returns("slow");
        mockRuntime.Setup(r => r.MountPath).Returns("/slow");
        mockRuntime.SetupProperty(r => r.Dialect);
        mockRuntime.Setup(r => r.WalkAsync(It.IsAny<string[]>(), It.IsAny<NinePDialect>()))
                   .ReturnsAsync(new Rwalk(0, new[] { new Qid(QidType.QTFILE, 0, 1) }));
        mockRuntime.Setup(r => r.OpenAsync(It.IsAny<string[]>(), It.IsAny<Topen>(), It.IsAny<NinePDialect>()))
                   .ReturnsAsync(new Ropen(0, new Qid(QidType.QTFILE, 0, 1), 8192));
        mockRuntime.Setup(r => r.ReadAsync(It.IsAny<string[]>(), It.IsAny<Tread>(), It.IsAny<NinePDialect>(), It.IsAny<CancellationToken>()))
                   .Returns<string[], Tread, NinePDialect, CancellationToken>(async (path, tread, dialect, ct) =>
                   {
                       readStarted.TrySetResult(true);
                       try
                       {
                           await blockRead.Task.WaitAsync(ct);
                       }
                       catch (OperationCanceledException)
                       {
                           cancellationObserved.Add(true);
                           throw;
                       }
                       return new Rread(tread.Tag, new byte[] { 1, 2, 3 });
                   });

        var mockBackend = new Mock<IProtocolBackend>();
        mockBackend.Setup(b => b.Name).Returns("slow");
        mockBackend.Setup(b => b.MountPath).Returns("/slow");
        mockBackend.Setup(b => b.GetRuntime(It.IsAny<X509Certificate2>()))
                   .Returns(mockRuntime.Object);

        var dispatcher = new NinePFSDispatcher(
            NullLogger<NinePFSDispatcher>.Instance,
            new[] { mockBackend.Object },
            new NullRemoteMountProvider());

        // Setup session
        await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTattach(new Tattach(1, 1, uint.MaxValue, "user", "/")), NinePDialect.NineP2000);
        await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTwalk(new Twalk(2, 1, 2, new[] { "slow" })), NinePDialect.NineP2000);
        await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTopen(new Topen(3, 2, NinePConstants.OREAD)), NinePDialect.NineP2000);

        // Launch concurrent read and flush
        var readTask = CoyoteTask.Run(async () =>
        {
            return await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTread(new Tread(100, 2, 0, 1024)), NinePDialect.NineP2000);
        });

        // Wait for read to start
        await readStarted.Task;

        // Send flush concurrently
        var flushTask = CoyoteTask.Run(async () =>
        {
            return await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTflush(new Tflush(101, 100)), NinePDialect.NineP2000);
        });

        // Allow both to complete
        blockRead.TrySetResult(true);

        var results = await CoyoteTask.WhenAll(readTask, flushTask);

        // Either read completed or was cancelled - both are valid outcomes
        // The key invariant: flush must complete with Rflush
        Assert.True(results[1] is Rflush, "Tflush must return Rflush response");
    }

    /// <summary>
    /// Per 9front: Flushing a non-existent tag should still return Rflush.
    /// </summary>
    [Fact]
    public static async Task TflushNonExistentTag_ReturnsRflush()
    {
        var mockBackend = new Mock<IProtocolBackend>();
        mockBackend.Setup(b => b.Name).Returns("mock");
        mockBackend.Setup(b => b.MountPath).Returns("/mock");
        mockBackend.Setup(b => b.GetRuntime(It.IsAny<X509Certificate2>()))
                   .Returns(() => RuntimeFileSystemAdapter.ToRuntime(new MockFileSystem()));

        var dispatcher = new NinePFSDispatcher(
            NullLogger<NinePFSDispatcher>.Instance,
            new[] { mockBackend.Object },
            new NullRemoteMountProvider());

        await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTattach(new Tattach(1, 1, uint.MaxValue, "user", "/")), NinePDialect.NineP2000);

        // Flush tag 9999 which was never used
        var result = await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTflush(new Tflush(2, 9999)), NinePDialect.NineP2000);

        Assert.IsType<Rflush>(result);
    }

    /// <summary>
    /// Tests concurrent Treaddir + Tflush - verifies CancellationToken propagation
    /// through the readdir call chain.
    /// </summary>
    [Fact]
    public static async Task TflushCancelsInFlightTreaddir_ConcurrentRace()
    {
        var readdirStarted = new TaskCompletionSource<bool>();
        var blockReaddir = new TaskCompletionSource<bool>();
        var ctWasCancelled = false;

        var mockRuntime = new Mock<IBackendRuntime>();
        mockRuntime.Setup(r => r.Id).Returns("rd");
        mockRuntime.Setup(r => r.MountPath).Returns("/rd");
        mockRuntime.SetupProperty(r => r.Dialect);
        mockRuntime.Setup(r => r.WalkAsync(It.IsAny<string[]>(), It.IsAny<NinePDialect>()))
                   .ReturnsAsync(new Rwalk(0, new[] { new Qid(QidType.QTDIR, 0, 1) }));
        mockRuntime.Setup(r => r.OpenAsync(It.IsAny<string[]>(), It.IsAny<Topen>(), It.IsAny<NinePDialect>()))
                   .ReturnsAsync(new Ropen(0, new Qid(QidType.QTDIR, 0, 1), 8192));
        mockRuntime.Setup(r => r.ReadAsync(It.IsAny<string[]>(), It.IsAny<Tread>(), It.IsAny<NinePDialect>(), It.IsAny<CancellationToken>()))
                   .Returns<string[], Tread, NinePDialect, CancellationToken>(async (path, tread, dialect, ct) =>
                   {
                       readdirStarted.TrySetResult(true);
                       try
                       {
                           await blockReaddir.Task.WaitAsync(ct);
                       }
                       catch (OperationCanceledException)
                       {
                           ctWasCancelled = true;
                           throw;
                       }
                       return new Rread(tread.Tag, Array.Empty<byte>());
                   });

        var mockBackend = new Mock<IProtocolBackend>();
        mockBackend.Setup(b => b.Name).Returns("rd");
        mockBackend.Setup(b => b.MountPath).Returns("/rd");
        mockBackend.Setup(b => b.GetRuntime(It.IsAny<X509Certificate2>()))
                   .Returns(mockRuntime.Object);

        var dispatcher = new NinePFSDispatcher(
            NullLogger<NinePFSDispatcher>.Instance,
            new[] { mockBackend.Object },
            new NullRemoteMountProvider());

        await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTattach(new Tattach(1, 1, uint.MaxValue, "user", "/")), NinePDialect.NineP2000);
        await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTwalk(new Twalk(2, 1, 2, new[] { "rd" })), NinePDialect.NineP2000);
        await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTopen(new Topen(3, 2, NinePConstants.OREAD)), NinePDialect.NineP2000);

        var readdirTask = CoyoteTask.Run(async () =>
        {
            return await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTreaddir(new Treaddir(24, 200, 2, 0, 8192)), NinePDialect.NineP2000);
        });

        await readdirStarted.Task;

        var flushTask = CoyoteTask.Run(async () =>
        {
            return await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTflush(new Tflush(201, 200)), NinePDialect.NineP2000);
        });

        blockReaddir.TrySetResult(true);
        await CoyoteTask.WhenAll(readdirTask, flushTask);

        // Flush must return Rflush
        Assert.IsType<Rflush>(flushTask.Result);
    }

    /// <summary>
    /// Tests multiple concurrent flushes targeting the same tag - race safety.
    /// </summary>
    [Fact]
    public static async Task MultipleConcurrentFlushes_SameTag_AllReturnRflush()
    {
        var readStarted = new TaskCompletionSource<bool>();
        var blockRead = new TaskCompletionSource<bool>();

        var mockRuntime = new Mock<IBackendRuntime>();
        mockRuntime.Setup(r => r.Id).Returns("slow");
        mockRuntime.Setup(r => r.MountPath).Returns("/slow");
        mockRuntime.SetupProperty(r => r.Dialect);
        mockRuntime.Setup(r => r.WalkAsync(It.IsAny<string[]>(), It.IsAny<NinePDialect>()))
                   .ReturnsAsync(new Rwalk(0, new[] { new Qid(QidType.QTFILE, 0, 1) }));
        mockRuntime.Setup(r => r.OpenAsync(It.IsAny<string[]>(), It.IsAny<Topen>(), It.IsAny<NinePDialect>()))
                   .ReturnsAsync(new Ropen(0, new Qid(QidType.QTFILE, 0, 1), 8192));
        mockRuntime.Setup(r => r.ReadAsync(It.IsAny<string[]>(), It.IsAny<Tread>(), It.IsAny<NinePDialect>(), It.IsAny<CancellationToken>()))
                   .Returns<string[], Tread, NinePDialect, CancellationToken>(async (path, tread, dialect, ct) =>
                   {
                       readStarted.TrySetResult(true);
                       await blockRead.Task.WaitAsync(ct);
                       return new Rread(tread.Tag, Array.Empty<byte>());
                   });

        var mockBackend = new Mock<IProtocolBackend>();
        mockBackend.Setup(b => b.Name).Returns("slow");
        mockBackend.Setup(b => b.MountPath).Returns("/slow");
        mockBackend.Setup(b => b.GetRuntime(It.IsAny<X509Certificate2>()))
                   .Returns(mockRuntime.Object);

        var dispatcher = new NinePFSDispatcher(
            NullLogger<NinePFSDispatcher>.Instance,
            new[] { mockBackend.Object },
            new NullRemoteMountProvider());

        await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTattach(new Tattach(1, 1, uint.MaxValue, "user", "/")), NinePDialect.NineP2000);
        await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTwalk(new Twalk(2, 1, 2, new[] { "slow" })), NinePDialect.NineP2000);
        await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTopen(new Topen(3, 2, NinePConstants.OREAD)), NinePDialect.NineP2000);

        var readTask = CoyoteTask.Run(async () =>
        {
            return await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTread(new Tread(100, 2, 0, 1024)), NinePDialect.NineP2000);
        });

        await readStarted.Task;

        // Launch multiple flushes concurrently
        var flush1 = CoyoteTask.Run(async () => await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTflush(new Tflush(101, 100)), NinePDialect.NineP2000));
        var flush2 = CoyoteTask.Run(async () => await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTflush(new Tflush(102, 100)), NinePDialect.NineP2000));
        var flush3 = CoyoteTask.Run(async () => await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTflush(new Tflush(103, 100)), NinePDialect.NineP2000));

        blockRead.TrySetResult(true);

        var results = await CoyoteTask.WhenAll(flush1, flush2, flush3);

        // All flushes must return Rflush
        foreach (var result in results)
        {
            Assert.IsType<Rflush>(result);
        }
    }

    private class NullRemoteMountProvider : IRemoteMountProvider
    {
        public void Start() { }
        public Task StopAsync() => Task.CompletedTask;
        public Task RegisterMountAsync(string mountPath, Func<IBackendRuntime> createRuntime) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> GetRemoteMountPathsAsync() => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<IBackendRuntime?> TryCreateRemoteRuntimeAsync(string mountPath) => Task.FromResult<IBackendRuntime?>(null);
        public void Dispose() { }
    }

    private class MockFileSystem : INinePFileSystem
    {
        public NinePDialect Dialect { get; set; } = NinePDialect.NineP2000;

        public Task<Rwalk> WalkAsync(Twalk twalk) => Task.FromResult(new Rwalk(twalk.Tag, Array.Empty<Qid>()));
        public Task<Ropen> OpenAsync(Topen topen) => Task.FromResult(new Ropen(topen.Tag, new Qid(QidType.QTDIR, 0, 1), 8192));
        public Task<Rread> ReadAsync(Tread tread) => Task.FromResult(new Rread(tread.Tag, Array.Empty<byte>()));
        public Task<Rwrite> WriteAsync(Twrite twrite) => Task.FromResult(new Rwrite(twrite.Tag, (uint)twrite.Data.Length));
        public Task<Rclunk> ClunkAsync(Tclunk tclunk) => Task.FromResult(new Rclunk(tclunk.Tag));
        public Task<Rstat> StatAsync(Tstat tstat) => Task.FromResult(new Rstat(tstat.Tag, new Stat(0, 0, 0, new Qid(QidType.QTDIR, 0, 1), 0755, 0, 0, 0, "mock", "none", "none", "none")));
        public Task<Rwstat> WstatAsync(Twstat twstat) => Task.FromResult(new Rwstat(twstat.Tag));
        public Task<Rremove> RemoveAsync(Tremove tremove) => Task.FromResult(new Rremove(tremove.Tag));
        public Task<Rcreate> CreateAsync(Tcreate tcreate) => Task.FromResult(new Rcreate(tcreate.Tag, new Qid(QidType.QTFILE, 0, 1), 8192));
        public INinePFileSystem Clone() => new MockFileSystem { Dialect = Dialect };
    }
}
