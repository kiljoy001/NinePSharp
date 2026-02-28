using System;
using System.Linq;
using FluentAssertions;
using FsCheck.Xunit;
using Microsoft.FSharp.Collections;
using Moq;
using NinePSharp.Constants;
using NinePSharp.Core.FSharp;
using NinePSharp.Server.Interfaces;
using Xunit;

namespace NinePSharp.Tests;

public sealed class ProtocolSessionNamespaceRulesTests
{
    [Fact]
    public void ProtocolSession_Process_Owns_The_Namespace()
    {
        var session = ProtocolSessionOps.create("s1", NinePDialect.NineP2000, null!);
        var path = NamespaceOps.splitPath("/srv");
        var mount = new Mount(path, new MountChain(MountIdForPath(path), FsBranches(BindFlags.MREPL, new[] { NewTarget("srv") })));
        var ns = new Namespace(FsList(new[] { mount }));

        var updated = ProtocolSessionOps.withNamespace(ns, session);

        updated.Process.Namespace.Should().BeSameAs(ns);
        ProtocolSessionOps.namespaceOf(updated).Should().BeSameAs(ns);
    }

    [Fact]
    public void ProtocolSession_Fid_Table_Stores_Channel_Directly()
    {
        var fidsProperty = typeof(ProtocolSession).GetProperty(nameof(ProtocolSession.Fids));

        fidsProperty.Should().NotBeNull();
        fidsProperty!.PropertyType.ToString().Should().Contain("Map`2");
        fidsProperty.PropertyType.GenericTypeArguments[1].Should().Be(typeof(Channel));
    }

    [Property(MaxTest = 50)]
    public bool BindFid_And_RemoveFid_RoundTrip_Preserves_Channel_Path(string[] rawSegments)
    {
        var session = ProtocolSessionOps.create("s2", NinePDialect.NineP2000, null!);
        var target = NewTarget("bind");
        var cleaned = (rawSegments ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => new string(s.Where(char.IsLetterOrDigit).Take(8).ToArray()))
            .Where(s => s.Length > 0)
            .Take(4)
            .ToArray();

        var channel = ChannelOps.createBackendNode(
            new NinePSharp.Core.FSharp.Qid(QidType.QTDIR, 0, 1),
            target,
            Array.Empty<string>(),
            cleaned);
        var withFid = ProtocolSessionOps.bindFid(42u, channel, session);
        var found = ProtocolSessionOps.tryFindFid(42u, withFid);
        var removed = ProtocolSessionOps.removeFid(42u, withFid);

        return found != null
            && found.Value.InternalPath.SequenceEqual(cleaned)
            && !ProtocolSessionOps.containsFid(42u, removed);
    }

    private static BackendTargetDescriptor NewTarget(string id)
        => BackendTargetDescriptor.Local(id, "/" + id, () => new Mock<INinePFileSystem>(MockBehavior.Loose).Object);

    private static FSharpList<T> FsList<T>(System.Collections.Generic.IEnumerable<T> items)
        => ListModule.OfSeq(items);

    private static FSharpList<MountBranch> FsBranches(BindFlags flags, System.Collections.Generic.IEnumerable<BackendTargetDescriptor> backends)
        => ListModule.OfSeq(backends.Select(target => new MountBranch(target, flags)));

    private static ulong MountIdForPath(System.Collections.Generic.IEnumerable<string> segments)
    {
        unchecked
        {
            ulong hash = 14695981039346656037UL;
            foreach (var segment in segments)
            {
                foreach (var ch in segment)
                {
                    hash = (hash ^ ch) * 1099511628211UL;
                }

                hash = (hash ^ '/') * 1099511628211UL;
            }

            return hash == 0 ? 1UL : hash;
        }
    }
}
