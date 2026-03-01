using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FsCheck.Xunit;
using Microsoft.FSharp.Collections;
using Moq;
using NinePSharp.Constants;
using NinePSharp.Core.FSharp;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;
using NinePSharp.Tests.Helpers;
using Xunit;

using FSharpQid = NinePSharp.Core.FSharp.Qid;

namespace NinePSharp.Tests.Architecture;

/// <summary>
/// Tests for 9front-correct namespace invariants.
///
/// Key 9front semantics tested:
/// - Path.mtpt is array of Chan* (mount point history)
/// - Mount lookup by channel identity (Type, Dev, Qid)
/// - Walk crosses mounts by checking qid at each step
/// </summary>
public sealed class NineFrontNamespaceInvariantPropertyTests
{
    // Tests for 9front-correct protocol and namespace behavior
    [Property(MaxTest = 50)]
    public bool Dispatcher_Unknown_Root_Walk_Does_Not_Bind_NewFid(string rawMissing)
    {
        string missing = CleanSegment(rawMissing, "missing");
        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[]
        {
            new StubBackend("/known", () => new MarkerFileSystem("marker:known"))
        });

        DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, tag: 1, fid: 100).Sync();
        var walkResponse = dispatcher.DispatchAsync(
            "test-session",
            NinePMessage.NewMsgTwalk(new Twalk(2, 100, 101, new[] { missing })),
            NinePDialect.NineP2000,
            null!).Sync();

        var readResponse = dispatcher.DispatchAsync(
            "test-session",
            NinePMessage.NewMsgTread(new Tread(3, 101, 0, 64)),
            NinePDialect.NineP2000,
            null!).Sync();

        return walkResponse is Rerror
            && readResponse is Rerror;
    }

    // NEW: Test correct qid-based mount lookup
    [Fact]
    public void Mount_Lookup_Uses_Channel_Identity_Not_Path()
    {
        var chan1 = CreateChannel(type: 1, dev: 100, qidPath: 1000);
        var chan2 = CreateChannel(type: 1, dev: 100, qidPath: 2000);

        var target1 = NewTarget("target1");
        var target2 = NewTarget("target2");

        var key1 = MountKeyModule.fromChannel(chan1);
        var key2 = MountKeyModule.fromChannel(chan2);

        var ns = NamespaceOps.empty;
        ns = NamespaceOps.mount(key1, CreateMhead(key1, target1), ns);
        ns = NamespaceOps.mount(key2, CreateMhead(key2, target2), ns);

        // Lookup by different qids finds different mounts
        var found1 = NamespaceOps.findMount(key1, ns);
        var found2 = NamespaceOps.findMount(key2, ns);

        found1.Value.Branches.ToList().Single().Target.Should().BeSameAs(target1);
        found2.Value.Branches.ToList().Single().Target.Should().BeSameAs(target2);
    }

    #region Helpers

    private static Channel CreateChannel(ushort type, uint dev, ulong qidPath)
    {
        var qid = new FSharpQid(QidType.QTDIR, 0, qidPath);
        var pathState = new PathState(
            FsList<string>(Array.Empty<string>()),
            FsList<Channel>(Array.Empty<Channel>()));
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

    private static List<string> CleanSegments(IEnumerable<string?> rawSegments, string fallback)
    {
        var cleaned = rawSegments
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select((s, i) => CleanSegment(s, fallback + i))
            .Distinct(StringComparer.Ordinal)
            .Take(4)
            .ToList();

        if (cleaned.Count == 0)
        {
            cleaned.Add(fallback);
        }

        return cleaned;
    }

    private static string CleanSegment(string? raw, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        var chars = raw.Where(char.IsLetterOrDigit).Take(12).ToArray();
        return chars.Length == 0 ? fallback : new string(chars);
    }

    private static BackendTargetDescriptor NewTarget(string id)
        => BackendTargetDescriptor.Local(id, "/" + id, () => new Mock<INinePFileSystem>(MockBehavior.Loose).Object);

    private static FSharpList<T> FsList<T>(IEnumerable<T> items)
        => ListModule.OfSeq(items);

    #endregion
}
