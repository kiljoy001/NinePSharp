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
    [Fact]
    public void Channel_Walk_Returns_New_Channel_Without_Mutating_Source()
    {
        var fs = NewFs();
        var source = new Channel(
            new NinePSharp.Core.FSharp.Qid(QidType.QTDIR, 1, 42),
            77UL,
            fs,
            FsList("eth"),
            true);

        var walked = ChannelOps.walk(FsList("wallet"), source);

        source.InternalPath.ToList().Should().Equal("eth");
        source.Offset.Should().Be(77UL);
        source.IsOpened.Should().BeTrue();

        walked.InternalPath.ToList().Should().Equal("eth", "wallet");
        walked.Offset.Should().Be(0UL);
        walked.IsOpened.Should().BeFalse();
        walked.FileSystem.Should().BeSameAs(fs);
    }

    [Property(MaxTest = 100)]
    public bool Channel_Walk_Fuzz_Preserves_Source_Immutability(string[] rawSegments)
    {
        if (rawSegments == null) return true;

        var fs = NewFs();
        var originalPath = FsList("root");
        var source = new Channel(
            new NinePSharp.Core.FSharp.Qid(QidType.QTDIR, 0, 7),
            5UL,
            fs,
            originalPath,
            true);

        var segments = rawSegments
            .Where(s => s != null)
            .Select(s => s.Replace("/", string.Empty))
            .Take(16)
            .ToList();

        var walked = ChannelOps.walk(FsList(segments), source);

        return source.InternalPath.SequenceEqual(new[] { "root" })
            && source.Offset == 5UL
            && source.IsOpened
            && walked.InternalPath.All(p => p.Length > 0 && p != "." && p != "..");
    }

    [Fact]
    public void Process_AddFd_Maps_Channel_Without_Mutating_Original_Process()
    {
        var fs = NewFs();
        var channel = new Channel(new NinePSharp.Core.FSharp.Qid(QidType.QTFILE, 0, 99), 0UL, fs, FsList("bin", "tool"), false);
        var proc = Process.create(1, EmptyNamespace());

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

        var sourceFs = NewFs();
        var targetFs = NewFs();
        var ns = BuildNamespace(
            MountAt("/" + source, sourceFs),
            MountAt("/" + target, targetFs));

        var channel = new Channel(new NinePSharp.Core.FSharp.Qid(QidType.QTDIR, 0, 11), 0UL, targetFs, FsList(target), false);
        var parent = Process.addFd(10, channel, Process.create(100, ns));

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

    private static NinePSharp.Core.FSharp.Namespace EmptyNamespace()
    {
        return NamespaceOps.empty;
    }

    private static Mount MountAt(string path, params INinePFileSystem[] backends)
    {
        return new Mount(NamespaceOps.splitPath(path), FsList(backends), BindFlags.MREPL);
    }

    private static NinePSharp.Core.FSharp.Namespace BuildNamespace(params Mount[] mounts)
    {
        return new NinePSharp.Core.FSharp.Namespace(FsList(mounts));
    }

    private static INinePFileSystem NewFs()
    {
        return new Mock<INinePFileSystem>(MockBehavior.Loose).Object;
    }

    private static FSharpList<T> FsList<T>(IEnumerable<T> items)
    {
        return ListModule.OfSeq(items);
    }

    private static FSharpList<string> FsList(params string[] items)
    {
        return ListModule.OfSeq(items);
    }

    private static string CleanPathSegment(string? raw, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        var chars = raw.Where(char.IsLetterOrDigit).Take(12).ToArray();
        return chars.Length == 0 ? fallback : new string(chars);
    }
}
