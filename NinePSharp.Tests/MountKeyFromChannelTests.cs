using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.FSharp.Collections;
using NinePSharp.Constants;
using NinePSharp.Core.FSharp;
using NinePSharp.Messages;
using NinePSharp.Server.Interfaces;
using NinePSharp.Tests.Helpers;
using Xunit;

using FSharpQid = NinePSharp.Core.FSharp.Qid;

namespace NinePSharp.Tests;

/// <summary>
/// Phase 2: Tests for mount key derivation from channels.
/// Verifies that mount resolution uses channel identity (Type, Dev, Qid) not paths.
/// </summary>
public class MountKeyFromChannelTests
{
    [Fact]
    public void MountKeyFromChannel_Uses_Channel_Type_Dev_Qid()
    {
        // Given channel with specific Type/Dev/Qid
        var qid = new FSharpQid(QidType.QTDIR, 99u, 0xCAFEBABE_DEADBEEFUL);
        var target = BackendTargetDescriptor.LocalRuntime("test", "/test", () => throw new NotImplementedException());
        var channel = ChannelOps.createBackendNode(qid, target, ListModule.Empty<string>(), ListModule.Empty<string>());

        // When mountKeyFromChannel called
        var mountKey = MountKeyModule.fromChannel(channel);

        // Then key matches channel's identity exactly
        mountKey.Type.Should().Be(channel.Type);
        mountKey.Dev.Should().Be(channel.Dev);
        mountKey.Qid.Type.Should().Be(channel.Qid.Type);
        mountKey.Qid.Version.Should().Be(channel.Qid.Version);
        mountKey.Qid.Path.Should().Be(channel.Qid.Path);
    }

    [Fact]
    public void FindMount_With_Channel_Key_Finds_Mounted_Backend()
    {
        // Arrange: Create a mount chain and namespace
        var target = BackendTargetDescriptor.LocalRuntime("backend1", "/mnt", () => throw new NotImplementedException());
        var mountPath = ListModule.OfSeq(new[] { "mnt" });

        // Create a channel that would be at this mount point
        var channelQid = new FSharpQid(QidType.QTDIR, 0u, 12345UL);
        var channel = ChannelOps.createBackendNode(channelQid, target, ListModule.Empty<string>(), mountPath);

        // Create mount key from channel identity
        var mountKey = MountKeyModule.fromChannel(channel);

        // Create mount chain at this key
        var branch = new MountBranch(target, BindFlags.MREPL);
        var chain = new MountChain(1UL, mountKey, mountPath, ListModule.OfSeq(new[] { branch }));

        // Mount in namespace
        var ns = NamespaceOps.mount(mountKey, chain, NamespaceOps.empty);

        // Act: FindMount with same channel identity
        var found = NamespaceOps.findMount(mountKey, ns);

        // Assert: Mount found
        found.Should().NotBeNull();
        found.Value.Should().Be(chain);
    }

    [Fact]
    public void Same_Path_Different_Qid_Are_Different_Mounts()
    {
        // Two backends at same path but with different Qids should be separate mount entries
        var target1 = BackendTargetDescriptor.LocalRuntime("backend1", "/shared", () => throw new NotImplementedException());
        var target2 = BackendTargetDescriptor.LocalRuntime("backend2", "/shared", () => throw new NotImplementedException());
        var mountPath = ListModule.OfSeq(new[] { "shared" });

        // Create channels with different Qids
        var qid1 = new FSharpQid(QidType.QTDIR, 0u, 111UL);
        var qid2 = new FSharpQid(QidType.QTDIR, 0u, 222UL);

        var channel1 = ChannelOps.createBackendNode(qid1, target1, ListModule.Empty<string>(), mountPath);
        var channel2 = ChannelOps.createBackendNode(qid2, target2, ListModule.Empty<string>(), mountPath);

        var key1 = MountKeyModule.fromChannel(channel1);
        var key2 = MountKeyModule.fromChannel(channel2);

        // Keys should be different due to different Qids
        key1.Should().NotBe(key2, "same path with different Qid should produce different mount keys");

        // Create separate mounts
        var branch1 = new MountBranch(target1, BindFlags.MREPL);
        var branch2 = new MountBranch(target2, BindFlags.MREPL);
        var chain1 = new MountChain(1UL, key1, mountPath, ListModule.OfSeq(new[] { branch1 }));
        var chain2 = new MountChain(2UL, key2, mountPath, ListModule.OfSeq(new[] { branch2 }));

        // Mount both in namespace
        var ns = NamespaceOps.empty;
        ns = NamespaceOps.mount(key1, chain1, ns);
        ns = NamespaceOps.mount(key2, chain2, ns);

        // Both mounts should exist independently
        var found1 = NamespaceOps.findMount(key1, ns);
        var found2 = NamespaceOps.findMount(key2, ns);

        found1.Should().NotBeNull();
        found2.Should().NotBeNull();
        found1.Value.Targets[0].Id.Should().Be("backend1");
        found2.Value.Targets[0].Id.Should().Be("backend2");
    }
}
