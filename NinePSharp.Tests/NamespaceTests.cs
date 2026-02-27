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

public class NamespaceTests
{
    [Fact]
    public void Bind_Replace_Effectively_Swaps_Directory_Backends()
    {
        var legacy = NewFs();
        var modern = NewFs();

        var ns = BuildNamespace(
            MountAt("/old", legacy),
            MountAt("/new", modern));

        var replaced = NamespaceOps.bind("/new", "/old", BindFlags.MREPL, ns);

        var (resolved, remaining) = NamespaceOps.resolve(FsList("old", "config.json"), replaced);
        resolved.ToList().Should().ContainSingle().Which.Should().BeSameAs(modern);
        remaining.ToList().Should().Equal("config.json");

        var (originalResolved, _) = NamespaceOps.resolve(FsList("old", "config.json"), ns);
        originalResolved.ToList().Should().ContainSingle().Which.Should().BeSameAs(legacy);
    }

    [Property(MaxTest = 80)]
    public bool Bind_Before_Union_Prioritizes_New_Backend(string sourceRaw, string targetRaw)
    {
        var source = CleanPathSegment(sourceRaw, "src");
        var target = CleanPathSegment(targetRaw, "dst");
        if (source == target) target += "_t";

        var oldBackend = NewFs();
        var newBackend = NewFs();

        var ns = BuildNamespace(
            MountAt("/" + source, newBackend),
            MountAt("/" + target, oldBackend));

        var bound = NamespaceOps.bind("/" + source, "/" + target, BindFlags.MBEFORE, ns);
        var (resolved, _) = NamespaceOps.resolve(FsList(target, "file"), bound);
        var order = resolved.ToList();

        return order.Count == 2
            && ReferenceEquals(order[0], newBackend)
            && ReferenceEquals(order[1], oldBackend);
    }

    [Property(MaxTest = 80)]
    public bool Bind_After_Union_Appends_New_Backend(string sourceRaw, string targetRaw)
    {
        var source = CleanPathSegment(sourceRaw, "src");
        var target = CleanPathSegment(targetRaw, "dst");
        if (source == target) target += "_t";

        var oldBackend = NewFs();
        var newBackend = NewFs();

        var ns = BuildNamespace(
            MountAt("/" + source, newBackend),
            MountAt("/" + target, oldBackend));

        var bound = NamespaceOps.bind("/" + source, "/" + target, BindFlags.MAFTER, ns);
        var (resolved, _) = NamespaceOps.resolve(FsList(target, "file"), bound);
        var order = resolved.ToList();

        return order.Count == 2
            && ReferenceEquals(order[0], oldBackend)
            && ReferenceEquals(order[1], newBackend);
    }

    [Property(MaxTest = 100)]
    public bool Resolve_Uses_Longest_Prefix_Match(string rootRaw, string childRaw, string leafRaw)
    {
        var root = CleanPathSegment(rootRaw, "root");
        var child = CleanPathSegment(childRaw, "child");
        var leaf = CleanPathSegment(leafRaw, "leaf");

        var rootFs = NewFs();
        var deepFs = NewFs();

        var ns = BuildNamespace(
            MountAt("/" + root, rootFs),
            MountAt("/" + root + "/" + child, deepFs));

        var (resolved, remaining) = NamespaceOps.resolve(FsList(root, child, leaf), ns);
        var list = resolved.ToList();

        return list.Count == 1
            && ReferenceEquals(list[0], deepFs)
            && remaining.SequenceEqual(new[] { leaf });
    }

    [Property(MaxTest = 100)]
    public bool SplitPath_Normalizes_Traversal_Tokens(string rawPath)
    {
        var parts = NamespaceOps.splitPath(rawPath ?? string.Empty).ToList();
        return parts.All(p => p.Length > 0 && p != "." && p != "..");
    }

    private static Mount MountAt(string path, params INinePFileSystem[] backends)
    {
        return new Mount(
            NamespaceOps.splitPath(path),
            FsList(backends),
            BindFlags.MREPL);
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
