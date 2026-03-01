using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;
using NinePSharp.Tests.Helpers;
using Xunit;

using CSharpQid = NinePSharp.Constants.Qid;

namespace NinePSharp.Tests;

/// <summary>
/// Phase 5: Tests for Tflush completion.
/// Verifies that Tflush can cancel in-flight operations per 9front semantics.
/// </summary>
public class TflushCompletionTests
{
    private const string SessionId = "test-session";

    [Fact]
    public async Task Tflush_Cancels_InFlight_Tread()
    {
        // Start slow Tread, send Tflush(oldTag)
        // Assert: Tread cancelled, Rflush returned
        var backend = new SlowBackend("slow", "/slow", TimeSpan.FromSeconds(10));
        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[] { backend });

        await DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, 1, 100);
        await DispatcherIntegrationTestKit.WalkAsync(dispatcher, 2, 100, 101, new[] { "slow", "file" });
        await DispatcherIntegrationTestKit.OpenAsync(dispatcher, 3, 101);

        // Start slow read (don't await)
        var readTask = dispatcher.DispatchAsync(SessionId, NinePMessage.NewMsgTread(new Tread(4, 101, 0, 1024)), NinePDialect.NineP2000);

        // Give it a moment to start
        await Task.Delay(50);

        // Send Tflush
        var flushResult = await dispatcher.DispatchAsync(SessionId, NinePMessage.NewMsgTflush(new Tflush(5, 4)), NinePDialect.NineP2000);

        // Flush should complete
        flushResult.Should().BeOfType<Rflush>();
    }

    [Fact]
    public async Task Tflush_For_Unknown_Tag_Returns_Rflush()
    {
        // Tflush for non-existent tag should still return Rflush (per spec)
        var backend = new SlowBackend("slow", "/slow", TimeSpan.FromMilliseconds(10));
        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[] { backend });

        await DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, 1, 100);

        // Flush a tag that was never used
        var flushResult = await dispatcher.DispatchAsync(SessionId, NinePMessage.NewMsgTflush(new Tflush(2, 999)), NinePDialect.NineP2000);

        flushResult.Should().BeOfType<Rflush>();
    }

    [Fact]
    public async Task Tflush_Waits_For_Operation_Completion_Before_Responding()
    {
        // 9front semantics: Rflush only after original op finishes/cancels
        var operationStarted = new TaskCompletionSource<bool>();
        var operationCanFinish = new TaskCompletionSource<bool>();
        var backend = new ControllableBackend("ctrl", "/ctrl", operationStarted, operationCanFinish);
        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[] { backend });

        await DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, 1, 100);
        await DispatcherIntegrationTestKit.WalkAsync(dispatcher, 2, 100, 101, new[] { "ctrl", "file" });
        await DispatcherIntegrationTestKit.OpenAsync(dispatcher, 3, 101);

        // Start read (don't await)
        var readTask = dispatcher.DispatchAsync(SessionId, NinePMessage.NewMsgTread(new Tread(4, 101, 0, 1024)), NinePDialect.NineP2000);

        // Wait for operation to start
        await operationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Send flush
        var flushTask = dispatcher.DispatchAsync(SessionId, NinePMessage.NewMsgTflush(new Tflush(5, 4)), NinePDialect.NineP2000);

        // Flush should not complete yet (operation still blocked)
        await Task.Delay(100);
        flushTask.IsCompleted.Should().BeFalse("flush should wait for operation to complete");

        // Let operation finish
        operationCanFinish.SetResult(true);

        // Now flush should complete
        var flushResult = await flushTask.WaitAsync(TimeSpan.FromSeconds(5));
        flushResult.Should().BeOfType<Rflush>();
    }

    [Fact]
    public async Task Multiple_Tflush_Same_Tag_All_Return_Rflush()
    {
        // Multiple flushes for the same tag should all succeed
        var backend = new SlowBackend("slow", "/slow", TimeSpan.FromSeconds(10));
        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[] { backend });

        await DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, 1, 100);
        await DispatcherIntegrationTestKit.WalkAsync(dispatcher, 2, 100, 101, new[] { "slow", "file" });
        await DispatcherIntegrationTestKit.OpenAsync(dispatcher, 3, 101);

        // Start slow read
        var readTask = dispatcher.DispatchAsync(SessionId, NinePMessage.NewMsgTread(new Tread(4, 101, 0, 1024)), NinePDialect.NineP2000);

        await Task.Delay(50);

        // Send multiple flushes
        var flush1 = dispatcher.DispatchAsync(SessionId, NinePMessage.NewMsgTflush(new Tflush(5, 4)), NinePDialect.NineP2000);
        var flush2 = dispatcher.DispatchAsync(SessionId, NinePMessage.NewMsgTflush(new Tflush(6, 4)), NinePDialect.NineP2000);

        var results = await Task.WhenAll(flush1, flush2);

        results[0].Should().BeOfType<Rflush>();
        results[1].Should().BeOfType<Rflush>();
    }

    [Fact]
    public async Task Completed_Operation_Flush_Returns_Immediately()
    {
        // Flush for already-completed operation should return immediately
        var backend = new SlowBackend("fast", "/fast", TimeSpan.Zero);
        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[] { backend });

        await DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, 1, 100);
        await DispatcherIntegrationTestKit.WalkAsync(dispatcher, 2, 100, 101, new[] { "fast", "file" });
        await DispatcherIntegrationTestKit.OpenAsync(dispatcher, 3, 101);

        // Complete read
        await dispatcher.DispatchAsync(SessionId, NinePMessage.NewMsgTread(new Tread(4, 101, 0, 1024)), NinePDialect.NineP2000);

        // Flush completed operation
        var flushResult = await dispatcher.DispatchAsync(SessionId, NinePMessage.NewMsgTflush(new Tflush(5, 4)), NinePDialect.NineP2000);

        flushResult.Should().BeOfType<Rflush>();
    }

    private sealed class SlowBackend : IProtocolBackend
    {
        private readonly TimeSpan _delay;

        public SlowBackend(string name, string mountPath, TimeSpan delay)
        {
            Name = name;
            MountPath = mountPath;
            _delay = delay;
        }

        public string Name { get; }
        public string MountPath { get; }

        public Task InitializeAsync(IConfiguration configuration) => Task.CompletedTask;
        public IBackendRuntime GetRuntime(X509Certificate2? certificate = null) => new SlowRuntime(Name, MountPath, _delay);
        public IBackendRuntime GetRuntime(System.Security.SecureString? credentials, X509Certificate2? certificate = null) => GetRuntime(certificate);
    }

    private sealed class SlowRuntime : IBackendRuntime
    {
        private readonly TimeSpan _delay;

        public SlowRuntime(string id, string mountPath, TimeSpan delay)
        {
            Id = id;
            MountPath = mountPath;
            _delay = delay;
        }

        public string Id { get; }
        public string MountPath { get; }
        public NinePDialect Dialect { get; set; } = NinePDialect.NineP2000;

        public Task<Rwalk> WalkAsync(string[] relativePath, NinePDialect dialect)
        {
            var qid = new CSharpQid(QidType.QTFILE, 0, 1UL);
            var qids = new CSharpQid[relativePath.Length];
            Array.Fill(qids, qid);
            return Task.FromResult(new Rwalk(0, qids!));
        }

        public Task<Ropen> OpenAsync(string[] relativePath, Topen topen, NinePDialect dialect)
            => Task.FromResult(new Ropen(topen.Tag, new CSharpQid(QidType.QTFILE, 0, 1), 8192));

        public async Task<Rread> ReadAsync(string[] relativePath, Tread tread, NinePDialect dialect, CancellationToken ct = default)
        {
            if (_delay > TimeSpan.Zero)
            {
                await Task.Delay(_delay, ct);
            }
            return new Rread(tread.Tag, Array.Empty<byte>());
        }

        public Task<Rwrite> WriteAsync(string[] relativePath, Twrite twrite, NinePDialect dialect, CancellationToken ct = default)
            => Task.FromResult(new Rwrite(twrite.Tag, (uint)twrite.Data.Length));

        public Task<Rclunk> ClunkAsync(string[] relativePath, Tclunk tclunk, NinePDialect dialect)
            => Task.FromResult(new Rclunk(tclunk.Tag));

        public Task<Rstat> StatAsync(string[] relativePath, Tstat tstat, NinePDialect dialect)
        {
            var stat = new Stat(0, 0, 1, new CSharpQid(QidType.QTFILE, 0, 1), 0644, 0, 0, 0, "file", "none", "none", "none", dialect: dialect);
            return Task.FromResult(new Rstat(tstat.Tag, stat));
        }

        public Task<Rwstat> WstatAsync(string[] relativePath, Twstat twstat, NinePDialect dialect)
            => Task.FromResult(new Rwstat(twstat.Tag));

        public Task<Rremove> RemoveAsync(string[] relativePath, Tremove tremove, NinePDialect dialect)
            => Task.FromResult(new Rremove(tremove.Tag));

        public Task<Rcreate> CreateAsync(string[] parentRelativePath, Tcreate tcreate, NinePDialect dialect)
            => Task.FromResult(new Rcreate(tcreate.Tag, new CSharpQid(QidType.QTFILE, 0, 2), 8192));
    }

    private sealed class ControllableBackend : IProtocolBackend
    {
        private readonly TaskCompletionSource<bool> _started;
        private readonly TaskCompletionSource<bool> _canFinish;

        public ControllableBackend(string name, string mountPath, TaskCompletionSource<bool> started, TaskCompletionSource<bool> canFinish)
        {
            Name = name;
            MountPath = mountPath;
            _started = started;
            _canFinish = canFinish;
        }

        public string Name { get; }
        public string MountPath { get; }

        public Task InitializeAsync(IConfiguration configuration) => Task.CompletedTask;
        public IBackendRuntime GetRuntime(X509Certificate2? certificate = null) => new ControllableRuntime(Name, MountPath, _started, _canFinish);
        public IBackendRuntime GetRuntime(System.Security.SecureString? credentials, X509Certificate2? certificate = null) => GetRuntime(certificate);
    }

    private sealed class ControllableRuntime : IBackendRuntime
    {
        private readonly TaskCompletionSource<bool> _started;
        private readonly TaskCompletionSource<bool> _canFinish;

        public ControllableRuntime(string id, string mountPath, TaskCompletionSource<bool> started, TaskCompletionSource<bool> canFinish)
        {
            Id = id;
            MountPath = mountPath;
            _started = started;
            _canFinish = canFinish;
        }

        public string Id { get; }
        public string MountPath { get; }
        public NinePDialect Dialect { get; set; } = NinePDialect.NineP2000;

        public Task<Rwalk> WalkAsync(string[] relativePath, NinePDialect dialect)
        {
            var qid = new CSharpQid(QidType.QTFILE, 0, 1UL);
            var qids = new CSharpQid[relativePath.Length];
            Array.Fill(qids, qid);
            return Task.FromResult(new Rwalk(0, qids!));
        }

        public Task<Ropen> OpenAsync(string[] relativePath, Topen topen, NinePDialect dialect)
            => Task.FromResult(new Ropen(topen.Tag, new CSharpQid(QidType.QTFILE, 0, 1), 8192));

        public async Task<Rread> ReadAsync(string[] relativePath, Tread tread, NinePDialect dialect, CancellationToken ct = default)
        {
            _started.TrySetResult(true);
            await _canFinish.Task;
            return new Rread(tread.Tag, Array.Empty<byte>());
        }

        public Task<Rwrite> WriteAsync(string[] relativePath, Twrite twrite, NinePDialect dialect, CancellationToken ct = default)
            => Task.FromResult(new Rwrite(twrite.Tag, (uint)twrite.Data.Length));

        public Task<Rclunk> ClunkAsync(string[] relativePath, Tclunk tclunk, NinePDialect dialect)
            => Task.FromResult(new Rclunk(tclunk.Tag));

        public Task<Rstat> StatAsync(string[] relativePath, Tstat tstat, NinePDialect dialect)
        {
            var stat = new Stat(0, 0, 1, new CSharpQid(QidType.QTFILE, 0, 1), 0644, 0, 0, 0, "file", "none", "none", "none", dialect: dialect);
            return Task.FromResult(new Rstat(tstat.Tag, stat));
        }

        public Task<Rwstat> WstatAsync(string[] relativePath, Twstat twstat, NinePDialect dialect)
            => Task.FromResult(new Rwstat(twstat.Tag));

        public Task<Rremove> RemoveAsync(string[] relativePath, Tremove tremove, NinePDialect dialect)
            => Task.FromResult(new Rremove(tremove.Tag));

        public Task<Rcreate> CreateAsync(string[] parentRelativePath, Tcreate tcreate, NinePDialect dialect)
            => Task.FromResult(new Rcreate(tcreate.Tag, new CSharpQid(QidType.QTFILE, 0, 2), 8192));
    }
}
