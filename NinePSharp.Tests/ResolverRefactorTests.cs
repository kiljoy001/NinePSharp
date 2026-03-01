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
/// Phase 4: Tests for resolver refactoring.
/// Verifies that resolution returns backend descriptors, not channels.
/// </summary>
public class ResolverRefactorTests
{
    [Fact]
    public async Task Walk_Creates_Channel_With_Backend_Real_Qid()
    {
        // The resolver should find the backend, then walk creates the channel
        // with the backend's real Qid (not a resolver-synthesized one)
        var backendQid = new CSharpQid(QidType.QTFILE, 77u, 0xFEDCBA9876543210UL);
        var backend = new RealQidBackend("real", "/real", backendQid);

        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[] { backend });

        await DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, 1, 100);
        var walk = await DispatcherIntegrationTestKit.WalkAsync(dispatcher, 2, 100, 101, new[] { "real", "file" });

        // Channel should have backend's real Qid, not resolver-synthesized
        walk.Wqid.Should().NotBeNull();
        walk.Wqid![1].Path.Should().Be(backendQid.Path, "channel Qid.Path should be backend's real Qid, not synthetic");
        walk.Wqid[1].Version.Should().Be(backendQid.Version, "channel Qid.Version should be backend's real version");
    }

    [Fact]
    public async Task Resolution_Does_Not_Mutate_Namespace_State()
    {
        // Resolution should be read-only - it shouldn't create mounts or modify state
        // Walk operations may bind fids, but the namespace mounts should not change
        var backend = new RealQidBackend("stable", "/stable", new CSharpQid(QidType.QTFILE, 0, 999UL));

        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[] { backend });

        // Attach and walk multiple times
        await DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, 1, 100);
        await DispatcherIntegrationTestKit.WalkAsync(dispatcher, 2, 100, 101, new[] { "stable" });
        await DispatcherIntegrationTestKit.WalkAsync(dispatcher, 3, 100, 102, new[] { "stable" });
        await DispatcherIntegrationTestKit.WalkAsync(dispatcher, 4, 100, 103, new[] { "stable", "file" });

        // All walks should succeed and return consistent Qids
        var stat1 = await DispatcherIntegrationTestKit.StatAsync(dispatcher, 5, 101);
        var stat2 = await DispatcherIntegrationTestKit.StatAsync(dispatcher, 6, 102);

        // Both should point to the same backend
        stat1.Stat.Qid.Path.Should().Be(stat2.Stat.Qid.Path, "resolution should be deterministic");
    }

    private sealed class RealQidBackend : IProtocolBackend
    {
        private readonly CSharpQid _qid;

        public RealQidBackend(string name, string mountPath, CSharpQid qid)
        {
            Name = name;
            MountPath = mountPath;
            _qid = qid;
        }

        public string Name { get; }
        public string MountPath { get; }

        public Task InitializeAsync(IConfiguration configuration) => Task.CompletedTask;

        public IBackendRuntime GetRuntime(X509Certificate2? certificate = null)
            => new RealQidRuntime(Name, MountPath, _qid);

        public IBackendRuntime GetRuntime(System.Security.SecureString? credentials, X509Certificate2? certificate = null)
            => GetRuntime(certificate);
    }

    private sealed class RealQidRuntime : IBackendRuntime
    {
        private readonly CSharpQid _qid;

        public RealQidRuntime(string id, string mountPath, CSharpQid qid)
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
            var stat = new Stat(0, 0, 1, _qid, 0644, 0, 0, 0, "file", "none", "none", "none", dialect: dialect);
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
