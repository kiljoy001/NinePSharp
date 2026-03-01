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

namespace NinePSharp.Tests.Architecture;

public class Plan9ProcessTests
{
    private static Channel RootChannel => ChannelOps.createNamespaceNode(new NinePSharp.Core.FSharp.Qid(QidType.QTDIR, 0, 0), FsList<string>(Array.Empty<string>()));

    [Fact]
    public void Channel_Walk_Returns_New_Channel_Without_Mutating_Source()
    {
        var target = NewTarget("eth");
        var source = ChannelOps.createBackendNode(
            new NinePSharp.Core.FSharp.Qid(QidType.QTDIR, 1, 42),
            target,
            Array.Empty<string>(),
            FsList("eth"));
        source = new Channel(source.Type, source.Dev, source.Qid, 77UL, source.Target, source.PathState, true, null, null, 0);

        var walked = ChannelOps.walk(FsList("wallet"), source);

        source.InternalPath.ToList().Should().Equal("eth");
        source.Offset.Should().Be(77UL);
        source.IsOpened.Should().BeTrue();

        walked.InternalPath.ToList().Should().Equal("eth", "wallet");
        walked.Offset.Should().Be(0UL);
        walked.IsOpened.Should().BeFalse();
        walked.Target.Should().Be(source.Target);
    }

    [Property(MaxTest = 100)]
    public bool Channel_Walk_Fuzz_Preserves_Source_Immutability(string[] rawSegments)
    {
        if (rawSegments == null) return true;

        var originalPath = FsList("root");
        var source = ChannelOps.createBackendNode(
            new NinePSharp.Core.FSharp.Qid(QidType.QTDIR, 0, 7),
            NewTarget("root"),
            Array.Empty<string>(),
            originalPath);
        source = new Channel(source.Type, source.Dev, source.Qid, 5UL, source.Target, source.PathState, true, null, null, 0);

        var segments = rawSegments
            .Where(s => s != null)
            .Select(s => s.Replace("/", string.Empty))
            .Take(16)
            .ToList();

        var walked = ChannelOps.walk(FsList<string>(segments), source);

        return source.InternalPath.SequenceEqual(new[] { "root" })
            && source.Offset == 5UL
            && source.IsOpened
            && walked.InternalPath.All(p => p.Length > 0 && p != "." && p != "..");
    }

    [Fact]
    public void Process_AddFd_Maps_Channel_Without_Mutating_Original_Process()
    {
        var channel = ChannelOps.createBackendNode(new NinePSharp.Core.FSharp.Qid(QidType.QTFILE, 0, 99), NewTarget("bin"), Array.Empty<string>(), FsList("bin", "tool"));
        var proc = Process.create(1, EmptyNamespace(), RootChannel);

        var mutated = Process.addFd(3, channel, proc);

        Process.tryGetChannel(3, proc).Should().BeNull();
        var mapped = Process.tryGetChannel(3, mutated);
        mapped.Should().NotBeNull();
        mapped!.Value.Should().Be(channel);
    }

    [Property(MaxTest = 80)]
    public bool Fork_Inherits_FdTable_And_Child_Bind_Does_Not_Leak(string sourceRaw, string targetRaw)
    {
        var source = CleanPathSegment(sourceRaw, "new");
        var target = CleanPathSegment(targetRaw, "old");
        if (source == target) target += "_t";

        var sourceFs = NewTarget("source");
        var targetFs = NewTarget("target");
        var ns = BuildNamespace(
            MountAt("/" + source, sourceFs),
            MountAt("/" + target, targetFs));

        var channel = ChannelOps.createBackendNode(new NinePSharp.Core.FSharp.Qid(QidType.QTDIR, 0, 11), targetFs, Array.Empty<string>(), FsList(target));
        var parent = Process.addFd(10, channel, Process.create(100, ns, RootChannel));

        var child = Process.fork(101, parent);
        var reboundChild = Process.bind("/" + source, "/" + target, BindFlags.MREPL, child);

        var parentResolved = NamespaceOps.resolve(FsList(target), parent.Namespace).Item1.ToList();
        var childResolved = NamespaceOps.resolve(FsList(target), reboundChild.Namespace).Item1.ToList();

        var parentFd = Process.tryGetChannel(10, parent);
        var childFd = Process.tryGetChannel(10, reboundChild);

        return parentResolved.Count == 1
            && childResolved.Count == 1
            && ReferenceEquals(parentResolved[0], targetFs)
            && ReferenceEquals(childResolved[0], sourceFs)
            && parentFd != null
            && childFd != null
            && ReferenceEquals(parentFd.Value, childFd.Value);
    }

    [Property(MaxTest = 100)]
    public bool Process_Unmount_Removes_Exact_Target(string pathRaw)
    {
        var path = CleanPathSegment(pathRaw, "mnt");
        var fs = NewTarget(path);
        var ns = BuildNamespace(MountAt("/" + path, fs));
        var proc = Process.create(1, ns, RootChannel);

        var unmountedProc = Process.unmount(NamespaceOps.mountKeyForPath(FsList(path)), proc);

        var originalResolved = NamespaceOps.resolve(FsList(path), proc.Namespace).Item1.ToList();
        var unmountedResolved = NamespaceOps.resolve(FsList(path), unmountedProc.Namespace).Item1.ToList();

        return originalResolved.Count == 1 && unmountedResolved.Count == 0;
    }

    [Property(MaxTest = 100)]
    public bool Process_Chdir_Updates_Dot(string pathRaw)
    {
        var path = CleanPathSegment(pathRaw, "dir");
        var root = RootChannel;
        var proc = Process.create(1, EmptyNamespace(), root);
        var newDot = ChannelOps.createNamespaceNode(new NinePSharp.Core.FSharp.Qid(QidType.QTDIR, 0, 1), FsList(path));

        var updatedProc = Process.chdir(newDot, proc);

        return ReferenceEquals(proc.Dot, root) && ReferenceEquals(updatedProc.Dot, newDot);
    }

    private static NinePSharp.Core.FSharp.Namespace EmptyNamespace()
    {
        return NamespaceOps.empty;
    }

    private static MountChain MountAt(string path, params BackendTargetDescriptor[] backends)
    {
        var normalized = NamespaceOps.splitPath(path);
        return new MountChain(
            MountIdForPath(normalized),
            NamespaceOps.mountKeyForPath(normalized),
            normalized,
            FsBranches(BindFlags.MREPL, backends));
    }

    private static NinePSharp.Core.FSharp.Namespace BuildNamespace(params MountChain[] mounts)
    {
        var ns = NamespaceOps.empty;
        foreach (var mount in mounts)
        {
            ns = NamespaceOps.mount(mount.From, mount, ns);
        }

        return ns;
    }

    private static BackendTargetDescriptor NewTarget(string id)
        => BackendTargetDescriptor.Local(id, "/" + id, () => new Mock<INinePFileSystem>(MockBehavior.Loose).Object);

    private static FSharpList<T> FsList<T>(IEnumerable<T> items)
    {
        return ListModule.OfSeq(items);
    }

    private static FSharpList<T> FsList<T>(params T[] items) => FsList((IEnumerable<T>)items);

    private static FSharpList<MountBranch> FsBranches(BindFlags flags, params BackendTargetDescriptor[] targets)
        => FsList(targets.Select(t => new MountBranch(t, flags)));

    private static string CleanPathSegment(string raw, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        var clean = raw.Replace("/", "").Trim();
        return string.IsNullOrWhiteSpace(clean) ? fallback : clean;
    }

    private static ulong MountIdForPath(IEnumerable<string> path)
        => (ulong)string.Join("/", path).GetHashCode();
}
