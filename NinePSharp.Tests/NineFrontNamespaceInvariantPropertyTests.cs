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

namespace NinePSharp.Tests.Architecture;

public sealed class NineFrontNamespaceInvariantPropertyTests
{
    [Property(MaxTest = 100)]
    public bool Channel_Walk_DotDot_From_Mounted_BackendRoot_Pops_Mount_History(string[] rawSegments)
    {
        var mountPath = CleanSegments(rawSegments, "mnt");
        ulong mountId = MountIdForPath(mountPath);
        var frame = new MountFrame(mountId, FsList(mountPath), FsList(mountPath.Take(Math.Max(0, mountPath.Count - 1))));
        var pathState = ChannelOps.createPathStateWithHistory(mountPath, new[] { frame });
        var channel = ChannelOps.createBackendNodeWithPathState(
            new NinePSharp.Core.FSharp.Qid(QidType.QTDIR, 0, 1),
            NewTarget("mounted"),
            Array.Empty<string>(),
            pathState);

        var walked = ChannelOps.walk(FsList(".."), channel);

        return walked.InternalPath.SequenceEqual(mountPath.Take(Math.Max(0, mountPath.Count - 1)))
            && !walked.PathState.MountHistory.Any()
            && walked.Offset == 0UL
            && !walked.IsOpened;
    }

    [Property(MaxTest = 80)]
    public bool Namespace_BindTarget_Mode_Requires_Exact_Target_Path(string rootRaw, string childRaw, string leafRaw)
    {
        string root = CleanSegment(rootRaw, "root");
        string child = CleanSegment(childRaw, "child");
        string leaf = CleanSegment(leafRaw, "leaf");
        if (child == root)
        {
            child += "x";
        }

        var shallow = NewTarget("shallow");
        var deep = NewTarget("deep");
        var ns = BuildNamespace(
            MountAt("/" + root, shallow),
            MountAt("/" + root + "/" + child, deep));

        var exact = NamespaceOps.resolveWithMode(LookupMode.BindTarget, FsList(root, child), ns);
        var nested = NamespaceOps.resolveWithMode(LookupMode.BindTarget, FsList(root, child, leaf), ns);

        var exactTargets = exact.Targets.ToList();
        return exactTargets.Count == 1
            && ReferenceEquals(exactTargets[0], deep)
            && !nested.Targets.Any()
            && nested.Remainder.SequenceEqual(new[] { root, child, leaf });
    }

    [Property(MaxTest = 80)]
    public bool Namespace_Walk_Mode_Still_Uses_Longest_Prefix_Under_Mount_Chains(string rootRaw, string childRaw, string leafRaw)
    {
        string root = CleanSegment(rootRaw, "root");
        string child = CleanSegment(childRaw, "child");
        string leaf = CleanSegment(leafRaw, "leaf");
        if (child == root)
        {
            child += "x";
        }

        var shallow = NewTarget("shallow");
        var deep = NewTarget("deep");
        var ns = BuildNamespace(
            MountAt("/" + root, shallow),
            MountAt("/" + root + "/" + child, deep));

        var resolved = NamespaceOps.resolveWithMode(LookupMode.Walk, FsList(root, child, leaf), ns);
        var targets = resolved.Targets.ToList();

        return targets.Count == 1
            && ReferenceEquals(targets[0], deep)
            && resolved.Remainder.SequenceEqual(new[] { leaf });
    }

    [Property(MaxTest = 50)]
    public bool Dispatcher_Unknown_Root_Walk_Does_Not_Bind_NewFid(string rawMissing)
    {
        string missing = CleanSegment(rawMissing, "missing");
        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[]
        {
            new StubBackend("/known", () => new MarkerFileSystem("marker:known"))
        });

        DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, tag: 1, fid: 100).Sync();
        var walk = DispatcherIntegrationTestKit.WalkAsync(dispatcher, tag: 2, fid: 100, newFid: 101, wname: new[] { missing }).Sync();

        var readResponse = dispatcher.DispatchAsync(
            NinePMessage.NewMsgTread(new Tread(3, 101, 0, 64)),
            NinePDialect.NineP2000U,
            null!).Sync();

        return (walk.Wqid == null || walk.Wqid.Length == 0)
            && readResponse is Rerror;
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

    private static Mount MountAt(string path, params BackendTargetDescriptor[] backends)
    {
        var normalized = NamespaceOps.splitPath(path);
        return new Mount(normalized, new MountChain(MountIdForPath(normalized), FsBranches(BindFlags.MREPL, backends)));
    }

    private static NinePSharp.Core.FSharp.Namespace BuildNamespace(params Mount[] mounts)
        => new(FsList(mounts));

    private static BackendTargetDescriptor NewTarget(string id)
        => BackendTargetDescriptor.Local(id, "/" + id, () => new Mock<INinePFileSystem>(MockBehavior.Loose).Object);

    private static FSharpList<T> FsList<T>(IEnumerable<T> items)
        => ListModule.OfSeq(items);

    private static FSharpList<string> FsList(params string[] items)
        => ListModule.OfSeq(items);

    private static FSharpList<MountBranch> FsBranches(BindFlags flags, IEnumerable<BackendTargetDescriptor> backends)
        => ListModule.OfSeq(backends.Select(target => new MountBranch(target, flags)));

    private static ulong MountIdForPath(IEnumerable<string> segments)
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
