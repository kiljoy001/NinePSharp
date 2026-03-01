using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;
using NinePSharp.Tests.Helpers;
using Xunit;

using CSharpQid = NinePSharp.Constants.Qid;

namespace NinePSharp.Tests;

/// <summary>
/// Phase 3: Tests for mount crossing with channel identity.
/// Verifies the dispatcher uses channel identity (not path) for mount resolution during walk.
/// </summary>
public class MountCrossingTests
{
    [Fact]
    public async Task Walk_Across_Mount_Uses_Channel_Identity_Not_Path()
    {
        // Setup: Mount backend B at path /mnt
        // Walk to /mnt should reach backend B
        // The mount crossing should use channel identity
        var expectedQid = new CSharpQid(QidType.QTFILE, 0, 0xABCD1234UL);
        var backend = new IdentityTrackingBackend("backend-b", "/mnt", expectedQid);

        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[] { backend });

        // Attach to root
        await DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, 1, 100);

        // Walk to /mnt/file - should cross mount and reach backend B
        var walkResult = await DispatcherIntegrationTestKit.WalkAsync(dispatcher, 2, 100, 101, new[] { "mnt", "file" });

        // Stat the file - should return backend B's Qid
        var stat = await DispatcherIntegrationTestKit.StatAsync(dispatcher, 3, 101);

        // Assert: mount was found and crossed correctly
        stat.Stat.Qid.Path.Should().Be(expectedQid.Path, "walk across mount should reach the mounted backend");
    }

    [Fact]
    public async Task Walk_Returns_Backend_Qid_In_Channel()
    {
        // Walk to backend file should return backend's real Qid, not synthetic
        var realQid = new CSharpQid(QidType.QTFILE, 42u, 0x123456789UL);
        var backend = new IdentityTrackingBackend("real-backend", "/real", realQid);

        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[] { backend });

        await DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, 1, 100);
        var walkResult = await DispatcherIntegrationTestKit.WalkAsync(dispatcher, 2, 100, 101, new[] { "real", "data" });

        // Walk result Qid should match what backend returned
        walkResult.Wqid.Should().NotBeNull();
        walkResult.Wqid!.Length.Should().Be(2);
        walkResult.Wqid[1].Path.Should().Be(realQid.Path, "channel.Qid should match backend's Rwalk Qid");
    }

    [Fact]
    public async Task Multiple_Backends_Same_Path_Distinguished_By_Qid()
    {
        // Two backends mounted at same virtual path with different identities
        // Should be distinguishable by their Qid values
        var qidA = new CSharpQid(QidType.QTFILE, 0, 111UL);
        var qidB = new CSharpQid(QidType.QTFILE, 0, 222UL);

        var backendA = new IdentityTrackingBackend("a", "/shared", qidA);
        var backendB = new IdentityTrackingBackend("b", "/shared", qidB);

        // Create dispatcher with both backends - first one wins in union
        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[] { backendA, backendB });

        await DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, 1, 100);
        var walk = await DispatcherIntegrationTestKit.WalkAsync(dispatcher, 2, 100, 101, new[] { "shared", "file" });

        // The walk should resolve to the first backend (A) in the union
        var stat = await DispatcherIntegrationTestKit.StatAsync(dispatcher, 3, 101);
        stat.Stat.Qid.Path.Should().Be(qidA.Path, "first backend in union should be used");
    }

    /// <summary>
    /// Backend that returns specific identity values for testing.
    /// </summary>
    private sealed class IdentityTrackingBackend : IProtocolBackend
    {
        private readonly CSharpQid _qid;

        public IdentityTrackingBackend(string name, string mountPath, CSharpQid qid)
        {
            Name = name;
            MountPath = mountPath;
            _qid = qid;
        }

        public string Name { get; }
        public string MountPath { get; }

        public Task InitializeAsync(IConfiguration configuration) => Task.CompletedTask;

        public IBackendRuntime GetRuntime(X509Certificate2? certificate = null)
            => new IdentityTrackingRuntime(Name, MountPath, _qid);

        public IBackendRuntime GetRuntime(System.Security.SecureString? credentials, X509Certificate2? certificate = null)
            => GetRuntime(certificate);
    }

    private sealed class IdentityTrackingRuntime : IBackendRuntime
    {
        private readonly CSharpQid _qid;

        public IdentityTrackingRuntime(string id, string mountPath, CSharpQid qid)
        {
            Id = id;
            MountPath = mountPath;
            _qid = qid;
        }

        public string Id { get; }
        public string MountPath { get; }
        public NinePDialect Dialect { get; set; } = NinePDialect.NineP2000;

        public Task<Rwalk> WalkAsync(string[] relativePath, NinePDialect dialect)
        {
            var qids = new CSharpQid[relativePath.Length];
            for (int i = 0; i < relativePath.Length; i++)
            {
                qids[i] = _qid;
            }
            return Task.FromResult(new Rwalk(0, qids!));
        }

        public Task<Ropen> OpenAsync(string[] relativePath, Topen topen, NinePDialect dialect)
            => Task.FromResult(new Ropen(topen.Tag, _qid, 8192));

        public Task<Rread> ReadAsync(string[] relativePath, Tread tread, NinePDialect dialect, System.Threading.CancellationToken ct = default)
            => Task.FromResult(new Rread(tread.Tag, Array.Empty<byte>()));

        public Task<Rwrite> WriteAsync(string[] relativePath, Twrite twrite, NinePDialect dialect, System.Threading.CancellationToken ct = default)
            => Task.FromResult(new Rwrite(twrite.Tag, (uint)twrite.Data.Length));

        public Task<Rclunk> ClunkAsync(string[] relativePath, Tclunk tclunk, NinePDialect dialect)
            => Task.FromResult(new Rclunk(tclunk.Tag));

        public Task<Rstat> StatAsync(string[] relativePath, Tstat tstat, NinePDialect dialect)
        {
            var dev = (uint)Math.Abs((Id + MountPath).GetHashCode());
            var stat = new Stat(0, 0, dev, _qid, 0644, 0, 0, 0, "file", "none", "none", "none", dialect: dialect);
            return Task.FromResult(new Rstat(tstat.Tag, stat));
        }

        public Task<Rwstat> WstatAsync(string[] relativePath, Twstat twstat, NinePDialect dialect)
            => Task.FromResult(new Rwstat(twstat.Tag));

        public Task<Rremove> RemoveAsync(string[] relativePath, Tremove tremove, NinePDialect dialect)
            => Task.FromResult(new Rremove(tremove.Tag));

        public Task<Rcreate> CreateAsync(string[] parentRelativePath, Tcreate tcreate, NinePDialect dialect)
            => Task.FromResult(new Rcreate(tcreate.Tag, _qid, 8192));
    }
}
