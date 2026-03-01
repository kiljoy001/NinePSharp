using NinePSharp.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FsCheck.Xunit;
using Microsoft.FSharp.Collections;
using Moq;
using NinePSharp.Core.FSharp;
using NinePSharp.Server.Interfaces;
using Xunit;

using FSharpQid = NinePSharp.Core.FSharp.Qid;

namespace NinePSharp.Tests.Architecture;

/// <summary>
/// Tests for 9front-correct namespace behavior.
///
/// Key 9front semantics:
/// - Mounts are keyed by channel identity (Type, Dev, Qid), NOT path strings
/// - Mount lookup is O(1) by MountKey, NOT O(n) path prefix scan
/// - Walk crosses mounts by checking qid at each step, NOT by prefix matching
/// </summary>
public class NamespaceTests
{
    // 9front chan.c:865 - findmount takes (type, dev, qid), not path
    [Fact]
    public void FindMount_Uses_MountKey_Not_Path()
    {
        var channel = CreateChannel(type: 1, dev: 100, qidPath: 1000);
        var target = NewTarget("backend");
        var key = MountKeyModule.fromChannel(channel);

        var ns = NamespaceOps.empty;
        var mhead = CreateMhead(key, target);
        ns = NamespaceOps.mount(key, mhead, ns);

        // Lookup by MountKey should find it
        var found = NamespaceOps.findMount(key, ns);
        found.Should().NotBeNull();
        found.Value.Branches.ToList().Should().ContainSingle()
            .Which.Target.Should().BeSameAs(target);
    }

    [Fact]
    public void FindMount_Different_Qid_Same_Path_Are_Different_Mounts()
    {
        // Two channels with same "path" but different qids should have separate mounts
        var chan1 = CreateChannel(type: 1, dev: 100, qidPath: 1000);
        var chan2 = CreateChannel(type: 1, dev: 100, qidPath: 2000); // Different qid

        var target1 = NewTarget("backend1");
        var target2 = NewTarget("backend2");

        var key1 = MountKeyModule.fromChannel(chan1);
        var key2 = MountKeyModule.fromChannel(chan2);

        var ns = NamespaceOps.empty;
        ns = NamespaceOps.mount(key1, CreateMhead(key1, target1), ns);
        ns = NamespaceOps.mount(key2, CreateMhead(key2, target2), ns);

        // Each should find its own mount
        var found1 = NamespaceOps.findMount(key1, ns);
        var found2 = NamespaceOps.findMount(key2, ns);

        found1.Value.Branches.Single().Target.Should().BeSameAs(target1);
        found2.Value.Branches.Single().Target.Should().BeSameAs(target2);
    }

    [Fact]
    public void FindMount_Different_Dev_Same_Qid_Are_Different_Mounts()
    {
        // Same qid but different dev should be different mounts
        var chan1 = CreateChannel(type: 1, dev: 100, qidPath: 1000);
        var chan2 = CreateChannel(type: 1, dev: 200, qidPath: 1000); // Different dev

        var target1 = NewTarget("backend1");
        var target2 = NewTarget("backend2");

        var key1 = MountKeyModule.fromChannel(chan1);
        var key2 = MountKeyModule.fromChannel(chan2);

        var ns = NamespaceOps.empty;
        ns = NamespaceOps.mount(key1, CreateMhead(key1, target1), ns);
        ns = NamespaceOps.mount(key2, CreateMhead(key2, target2), ns);

        var found1 = NamespaceOps.findMount(key1, ns);
        var found2 = NamespaceOps.findMount(key2, ns);

        found1.Value.Branches.Single().Target.Should().BeSameAs(target1);
        found2.Value.Branches.Single().Target.Should().BeSameAs(target2);
    }

    [Fact]
    public void Union_Mount_MBEFORE_Prepends_To_Chain()
    {
        var channel = CreateChannel(type: 1, dev: 100, qidPath: 1000);
        var key = MountKeyModule.fromChannel(channel);

        var oldTarget = NewTarget("old");
        var newTarget = NewTarget("new");

        var ns = NamespaceOps.empty;
        ns = NamespaceOps.mount(key, CreateMhead(key, oldTarget), ns);

        // Mount new target BEFORE
        var newBranch = new MountBranch(newTarget, BindFlags.MBEFORE);
        var existingChain = NamespaceOps.findMount(key, ns).Value;
        var updatedChain = new MountChain(
            existingChain.MountId,
            existingChain.From,
            existingChain.MountPath,
            FsList(new[] { newBranch }.Concat(existingChain.Branches)));
        ns = NamespaceOps.mount(key, updatedChain, ns);

        var found = NamespaceOps.findMount(key, ns);
        var targets = found.Value.Branches.Select(b => b.Target).ToList();

        targets.Should().HaveCount(2);
        targets[0].Should().BeSameAs(newTarget); // New is first
        targets[1].Should().BeSameAs(oldTarget);
    }

    [Fact]
    public void Union_Mount_MAFTER_Appends_To_Chain()
    {
        var channel = CreateChannel(type: 1, dev: 100, qidPath: 1000);
        var key = MountKeyModule.fromChannel(channel);

        var oldTarget = NewTarget("old");
        var newTarget = NewTarget("new");

        var ns = NamespaceOps.empty;
        ns = NamespaceOps.mount(key, CreateMhead(key, oldTarget), ns);

        // Mount new target AFTER
        var newBranch = new MountBranch(newTarget, BindFlags.MAFTER);
        var existingChain = NamespaceOps.findMount(key, ns).Value;
        var updatedChain = new MountChain(
            existingChain.MountId,
            existingChain.From,
            existingChain.MountPath,
            FsList(existingChain.Branches.Concat(new[] { newBranch })));
        ns = NamespaceOps.mount(key, updatedChain, ns);

        var found = NamespaceOps.findMount(key, ns);
        var targets = found.Value.Branches.Select(b => b.Target).ToList();

        targets.Should().HaveCount(2);
        targets[0].Should().BeSameAs(oldTarget);
        targets[1].Should().BeSameAs(newTarget); // New is last
    }

    [Fact]
    public void Unmount_Removes_Mount()
    {
        var channel = CreateChannel(type: 1, dev: 100, qidPath: 1000);
        var key = MountKeyModule.fromChannel(channel);
        var target = NewTarget("backend");

        var ns = NamespaceOps.empty;
        ns = NamespaceOps.mount(key, CreateMhead(key, target), ns);

        NamespaceOps.findMount(key, ns).Should().NotBeNull();

        ns = NamespaceOps.unmount(key, ns);

        NamespaceOps.findMount(key, ns).Should().BeNull();
    }

    #region Helpers

    private static Channel CreateChannel(ushort type, uint dev, ulong qidPath)
    {
        var qid = new FSharpQid(QidType.QTDIR, 0, qidPath);
        var pathState = new PathState(FsList<string>(Array.Empty<string>()), FsList<Channel>(Array.Empty<Channel>()));
        return new Channel(
            type,
            dev,
            qid,
            0UL,
            ChannelTarget.NamespaceNode,
            pathState,
            false,
            Microsoft.FSharp.Core.FSharpOption<MountChain>.None,
            Microsoft.FSharp.Core.FSharpOption<Channel>.None,
            0);
    }

    private static MountChain CreateMhead(MountKey key, BackendTargetDescriptor target)
    {
        var branch = new MountBranch(target, BindFlags.MREPL);
        return new MountChain(
            1UL,
            key,
            FsList<string>(Array.Empty<string>()),
            FsList(new[] { branch }));
    }

    private static BackendTargetDescriptor NewTarget(string id)
        => BackendTargetDescriptor.Local(id, "/" + id, () => new Mock<INinePFileSystem>(MockBehavior.Loose).Object);

    private static FSharpList<T> FsList<T>(IEnumerable<T> items)
        => ListModule.OfSeq(items);

    #endregion
}
