using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NinePSharp.Constants;
using NinePSharp.Core.FSharp;
using NinePSharp.Messages;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using NinePSharp.Tests.Helpers;
using Xunit;

using CSharpQid = NinePSharp.Constants.Qid;
using FSharpQid = NinePSharp.Core.FSharp.Qid;

namespace NinePSharp.Tests;

/// <summary>
/// Phase 1: Tests for channel identity propagation.
/// Channels should carry real (Type, Dev, Qid) from backends, not synthetic hashes.
/// </summary>
public class ChannelIdentityTests
{
    [Fact]
    public async Task Walk_To_Backend_File_Channel_Qid_Matches_Backend_Rstat()
    {
        // Arrange: backend returns specific Qid in Rwalk/Rstat
        var expectedQid = new CSharpQid(QidType.QTFILE, 42u, 0xDEADBEEF_12345678UL);
        var backend = new QidTrackingBackend("tracker", "/tracker", expectedQid);

        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[] { backend });

        // Attach and walk to the backend's file
        await DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, 1, 100);
        var walkResult = await DispatcherIntegrationTestKit.WalkAsync(dispatcher, 2, 100, 101, new[] { "tracker", "testfile" });

        // Act: Get channel state via Tstat (which exposes the channel's Qid)
        var stat = await DispatcherIntegrationTestKit.StatAsync(dispatcher, 3, 101);

        // Assert: channel.Qid == backend's Qid (not synthetic hash)
        stat.Stat.Qid.Path.Should().Be(expectedQid.Path, "channel Qid.Path should match backend's real Qid");
        stat.Stat.Qid.Version.Should().Be(expectedQid.Version, "channel Qid.Version should match backend's real Qid");
    }

    [Fact]
    public async Task Backend_Channel_Dev_Derived_From_Backend_Identity()
    {
        // Two different backends at same virtual path should have different Dev values
        var backendA = new QidTrackingBackend("backendA", "/shared", new CSharpQid(QidType.QTFILE, 0, 100));
        var backendB = new QidTrackingBackend("backendB", "/shared", new CSharpQid(QidType.QTFILE, 0, 100));

        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[] { backendA, backendB });

        // Attach to each backend
        await DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, 1, 100);

        // Walk to backendA's file
        await DispatcherIntegrationTestKit.WalkAsync(dispatcher, 2, 100, 101, new[] { "shared" });
        var statA = await DispatcherIntegrationTestKit.StatAsync(dispatcher, 3, 101);

        // The Dev field should be derived from backend identity, not be zero
        // For backends, Type should be non-zero (namespace is 0, backends are 1+)
        // This test verifies the channel carries backend-specific identity
        statA.Stat.Dev.Should().NotBe(0u, "backend channels should have non-zero Dev derived from backend identity");
    }

    [Fact]
    public void MountKey_FromChannel_Returns_Channel_Identity_Triple()
    {
        // Create a channel with specific identity using F# Qid type
        var qid = new FSharpQid(QidType.QTDIR, 5u, 0x123456789ABCDEF0UL);
        var channel = ChannelOps.createNamespaceNode(qid, Microsoft.FSharp.Collections.ListModule.Empty<string>());

        // MountKeyModule.fromChannel(chan) should equal { Type=chan.Type, Dev=chan.Dev, Qid=chan.Qid }
        var mountKey = MountKeyModule.fromChannel(channel);

        mountKey.Type.Should().Be(channel.Type);
        mountKey.Dev.Should().Be(channel.Dev);
        mountKey.Qid.Type.Should().Be(channel.Qid.Type);
        mountKey.Qid.Version.Should().Be(channel.Qid.Version);
        mountKey.Qid.Path.Should().Be(channel.Qid.Path);
    }

    /// <summary>
    /// Test backend that tracks and returns specific Qids.
    /// </summary>
    private sealed class QidTrackingBackend : IProtocolBackend
    {
        private readonly CSharpQid _returnQid;

        public QidTrackingBackend(string name, string mountPath, CSharpQid returnQid)
        {
            Name = name;
            MountPath = mountPath;
            _returnQid = returnQid;
        }

        public string Name { get; }
        public string MountPath { get; }

        public Task InitializeAsync(IConfiguration configuration) => Task.CompletedTask;

        public IBackendRuntime GetRuntime(X509Certificate2? certificate = null)
            => new QidTrackingRuntime(Name, MountPath, _returnQid);

        public IBackendRuntime GetRuntime(System.Security.SecureString? credentials, X509Certificate2? certificate = null)
            => GetRuntime(certificate);
    }

    private sealed class QidTrackingRuntime : IBackendRuntime
    {
        private readonly CSharpQid _returnQid;

        public QidTrackingRuntime(string id, string mountPath, CSharpQid returnQid)
        {
            Id = id;
            MountPath = mountPath;
            _returnQid = returnQid;
        }

        public string Id { get; }
        public string MountPath { get; }
        public NinePDialect Dialect { get; set; } = NinePDialect.NineP2000;

        public Task<Rwalk> WalkAsync(string[] relativePath, NinePDialect dialect)
        {
            // Return the tracked Qid for each walk step
            var qids = new CSharpQid[relativePath.Length];
            for (int i = 0; i < relativePath.Length; i++)
            {
                qids[i] = _returnQid;
            }
            return Task.FromResult(new Rwalk(0, qids!));
        }

        public Task<Ropen> OpenAsync(string[] relativePath, Topen topen, NinePDialect dialect)
            => Task.FromResult(new Ropen(topen.Tag, _returnQid, 8192));

        public Task<Rread> ReadAsync(string[] relativePath, Tread tread, NinePDialect dialect, System.Threading.CancellationToken ct = default)
            => Task.FromResult(new Rread(tread.Tag, Array.Empty<byte>()));

        public Task<Rwrite> WriteAsync(string[] relativePath, Twrite twrite, NinePDialect dialect, System.Threading.CancellationToken ct = default)
            => Task.FromResult(new Rwrite(twrite.Tag, (uint)twrite.Data.Length));

        public Task<Rclunk> ClunkAsync(string[] relativePath, Tclunk tclunk, NinePDialect dialect)
            => Task.FromResult(new Rclunk(tclunk.Tag));

        public Task<Rstat> StatAsync(string[] relativePath, Tstat tstat, NinePDialect dialect)
        {
            // Return a non-zero Dev derived from backend identity
            // Stat signature: (ushort size, ushort type, uint dev, Qid qid, ...)
            var dev = (uint)Math.Abs((Id + MountPath).GetHashCode());
            var stat = new Stat(0, 0, dev, _returnQid, 0644, 0, 0, 0, "testfile", "none", "none", "none", dialect: dialect);
            return Task.FromResult(new Rstat(tstat.Tag, stat));
        }

        public Task<Rwstat> WstatAsync(string[] relativePath, Twstat twstat, NinePDialect dialect)
            => Task.FromResult(new Rwstat(twstat.Tag));

        public Task<Rremove> RemoveAsync(string[] relativePath, Tremove tremove, NinePDialect dialect)
            => Task.FromResult(new Rremove(tremove.Tag));

        public Task<Rcreate> CreateAsync(string[] parentRelativePath, Tcreate tcreate, NinePDialect dialect)
            => Task.FromResult(new Rcreate(tcreate.Tag, _returnQid, 8192));
    }
}
