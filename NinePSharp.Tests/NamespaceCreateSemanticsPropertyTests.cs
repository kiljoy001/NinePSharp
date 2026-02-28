using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FluentAssertions;
using FsCheck.Xunit;
using Microsoft.FSharp.Collections;
using Moq;
using NinePSharp.Constants;
using NinePSharp.Core.FSharp;
using NinePSharp.Server.Interfaces;
using NinePSharp.Tests.Helpers;
using Xunit;

namespace NinePSharp.Tests.Architecture;

public sealed class NamespaceCreateSemanticsPropertyTests
{
    [Fact]
    public void Namespace_Create_Selects_First_Mcreate_Branch()
    {
        var stale = NewTarget("stale");
        var writable = NewTarget("writable");
        var mountPath = NamespaceOps.splitPath("/union");
        var ns = new NinePSharp.Core.FSharp.Namespace(FsList(new[]
        {
            new Mount(
                mountPath,
                new MountChain(
                    MountIdForPath(mountPath),
                    FsBranches(
                        new MountBranch(stale, BindFlags.MAFTER),
                        new MountBranch(writable, BindFlags.MCREATE | BindFlags.MAFTER))))
        }));

        var chosen = NamespaceOps.trySelectCreateTarget(FsList("union"), ns);

        chosen.Should().NotBeNull();
        chosen.Value.Should().BeSameAs(writable);
    }

    [Fact]
    public void Namespace_Create_Rejects_Ambiguous_Union_Without_Mcreate()
    {
        var first = NewTarget("first");
        var second = NewTarget("second");
        var mountPath = NamespaceOps.splitPath("/union");
        var ns = new NinePSharp.Core.FSharp.Namespace(FsList(new[]
        {
            new Mount(
                mountPath,
                new MountChain(
                    MountIdForPath(mountPath),
                    FsBranches(
                        new MountBranch(first, BindFlags.MBEFORE),
                        new MountBranch(second, BindFlags.MAFTER))))
        }));

        NamespaceOps.trySelectCreateTarget(FsList("union"), ns).Should().BeNull();
    }

    [Fact]
    public void Dispatcher_Create_Rebinds_Fid_To_Created_File()
    {
        var prototype = new SharedMutableFileSystem();
        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[]
        {
            new StubBackend("/mock", () => prototype.Clone())
        });

        DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, tag: 1, fid: 100).Sync();
        DispatcherIntegrationTestKit.WalkAsync(dispatcher, tag: 2, fid: 100, newFid: 101, wname: new[] { "mock" }).Sync();
        DispatcherIntegrationTestKit.CreateAsync(dispatcher, tag: 3, fid: 101, name: "newfile").Sync();
        DispatcherIntegrationTestKit.WriteAsync(dispatcher, tag: 4, fid: 101, offset: 0, data: Encoding.UTF8.GetBytes("payload")).Sync();
        var read = DispatcherIntegrationTestKit.ReadAsync(dispatcher, tag: 5, fid: 101, offset: 0, count: 128).Sync();

        DispatcherIntegrationTestKit.ReadPayload(read).Should().Be("payload");
    }

    [Fact]
    public void Dispatcher_Root_Directory_Listing_Preserves_Namespace_Order()
    {
        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[]
        {
            new StubBackend("/zeta", () => new MarkerFileSystem("zeta")),
            new StubBackend("/alpha", () => new MarkerFileSystem("alpha")),
            new StubBackend("/middle", () => new MarkerFileSystem("middle"))
        });

        DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, tag: 1, fid: 200).Sync();
        var page = DispatcherIntegrationTestKit.ReaddirAsync(dispatcher, tag: 2, fid: 200, offset: 0, count: 512).Sync();
        var names = DispatcherIntegrationTestKit.ParseReaddirEntries(page.Data.Span).Select(e => e.Name).ToArray();

        names.Should().StartWith(new[] { "zeta", "alpha", "middle" });
    }

    [Fact]
    public void Dispatcher_UnionMount_Readdir_Merges_Branch_Entries_In_Order()
    {
        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[]
        {
            new StubBackend("/union", () => new DirectoryListingFileSystem(new[] { "alpha", "shared" })),
            new StubBackend("/union", () => new DirectoryListingFileSystem(new[] { "beta", "shared", "omega" }))
        });

        DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, tag: 1, fid: 300).Sync();
        DispatcherIntegrationTestKit.WalkAsync(dispatcher, tag: 2, fid: 300, newFid: 301, wname: new[] { "union" }).Sync();
        var page = DispatcherIntegrationTestKit.ReaddirAsync(dispatcher, tag: 3, fid: 301, offset: 0, count: 1024).Sync();
        var names = DispatcherIntegrationTestKit.ParseReaddirEntries(page.Data.Span).Select(e => e.Name).ToArray();

        names.Should().Equal("alpha", "shared", "beta", "omega");
    }

    [Fact]
    public void Dispatcher_UnionMount_Read_StatsTable_Merges_Branch_Entries_In_Order()
    {
        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[]
        {
            new StubBackend("/union", () => new DirectoryListingFileSystem(new[] { "alpha", "shared" })),
            new StubBackend("/union", () => new DirectoryListingFileSystem(new[] { "beta", "shared", "omega" }))
        });

        DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, tag: 1, fid: 310).Sync();
        DispatcherIntegrationTestKit.WalkAsync(dispatcher, tag: 2, fid: 310, newFid: 311, wname: new[] { "union" }).Sync();
        var page = DispatcherIntegrationTestKit.ReadAsync(dispatcher, tag: 3, fid: 311, offset: 0, count: 4096).Sync();
        var names = DispatcherIntegrationTestKit.ParseStatsTable(page.Data.Span).Select(s => s.Name).ToArray();

        names.Should().Equal("alpha", "shared", "beta", "omega");
    }

    [Fact]
    public void Namespace_Create_Selects_First_Creatable_Branch_On_Nested_Bound_Path()
    {
        var left = NewTarget("left");
        var right = NewTarget("right");
        var existing = NewTarget("existing");

        var ns = new NinePSharp.Core.FSharp.Namespace(FsList(new[]
        {
            MountAt("/src/left", left),
            MountAt("/src/right", right),
            MountAt("/top/union", existing)
        }));

        var bound = NamespaceOps.bind("/src/left", "/top/union", BindFlags.MBEFORE | BindFlags.MCREATE, ns);
        bound = NamespaceOps.bind("/src/right", "/top/union", BindFlags.MAFTER | BindFlags.MCREATE, bound);

        var chosen = NamespaceOps.trySelectCreateTarget(FsList("top", "union"), bound);
        chosen.Should().NotBeNull();
        chosen.Value.Should().BeSameAs(left);
    }

    [Fact]
    public void Dispatcher_UnionWalk_Falls_Through_To_First_Branch_Containing_Child()
    {
        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[]
        {
            new StubBackend("/union", () => new ExistingPathFileSystem(Array.Empty<string>())),
            new StubBackend("/union", () => new ExistingPathFileSystem(new[] { "/target" }))
        });

        DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, tag: 1, fid: 400).Sync();
        DispatcherIntegrationTestKit.WalkAsync(dispatcher, tag: 2, fid: 400, newFid: 401, wname: new[] { "union" }).Sync();
        var walk = DispatcherIntegrationTestKit.WalkAsync(dispatcher, tag: 3, fid: 401, newFid: 402, wname: new[] { "target" }).Sync();
        var read = DispatcherIntegrationTestKit.ReadAsync(dispatcher, tag: 4, fid: 402, offset: 0, count: 128).Sync();

        walk.Wqid.Should().HaveCount(1);
        DispatcherIntegrationTestKit.ReadPayload(read).Should().Be("/target");
    }

    [Fact]
    public void Dispatcher_UnionWalk_Prefers_Deeper_Namespace_Mount_Over_Backend_Branches()
    {
        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[]
        {
            new StubBackend("/union", () => new ExistingPathFileSystem(new[] { "/child" })),
            new StubBackend("/union/child", () => new DirectoryListingFileSystem(new[] { "nested" }))
        });

        DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, tag: 1, fid: 410).Sync();
        DispatcherIntegrationTestKit.WalkAsync(dispatcher, tag: 2, fid: 410, newFid: 411, wname: new[] { "union" }).Sync();
        DispatcherIntegrationTestKit.WalkAsync(dispatcher, tag: 3, fid: 411, newFid: 412, wname: new[] { "child" }).Sync();
        var page = DispatcherIntegrationTestKit.ReaddirAsync(dispatcher, tag: 4, fid: 412, offset: 0, count: 512).Sync();
        var names = DispatcherIntegrationTestKit.ParseReaddirEntries(page.Data.Span).Select(e => e.Name).ToArray();

        names.Should().Equal("nested");
    }

    [Property(MaxTest = 60)]
    public bool Namespace_Create_Target_Is_Stable_Under_Repeated_Resolution(string[] rawNames)
    {
        var names = CleanSegments(rawNames, "m");
        if (names.Count < 2)
        {
            names.Add("fallback");
        }

        var mountPath = NamespaceOps.splitPath("/create");
        var branches = new List<MountBranch>();
        for (int i = 0; i < names.Count; i++)
        {
            var target = NewTarget(names[i]);
            var flags = i == names.Count - 1 ? BindFlags.MCREATE | BindFlags.MAFTER : BindFlags.MAFTER;
            branches.Add(new MountBranch(target, flags));
        }

        var ns = new NinePSharp.Core.FSharp.Namespace(FsList(new[]
        {
            new Mount(mountPath, new MountChain(MountIdForPath(mountPath), FsBranches(branches.ToArray())))
        }));

        var first = NamespaceOps.trySelectCreateTarget(FsList("create"), ns);
        var second = NamespaceOps.trySelectCreateTarget(FsList("create"), ns);

        return first != null && second != null && ReferenceEquals(first.Value, second.Value);
    }

    private static List<string> CleanSegments(IEnumerable<string?> rawNames, string fallback)
    {
        return rawNames
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select((s, i) => CleanSegment(s, fallback + i))
            .Distinct(StringComparer.Ordinal)
            .Take(4)
            .ToList();
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
        return new Mount(normalized, new MountChain(MountIdForPath(normalized), FsBranches(backends.Select(target => new MountBranch(target, BindFlags.MREPL)).ToArray())));
    }

    private static BackendTargetDescriptor NewTarget(string id)
        => BackendTargetDescriptor.Local(id, "/" + id, () => new Mock<INinePFileSystem>(MockBehavior.Loose).Object);

    private static FSharpList<T> FsList<T>(IEnumerable<T> items)
        => ListModule.OfSeq(items);

    private static FSharpList<string> FsList(params string[] items)
        => ListModule.OfSeq(items);

    private static FSharpList<MountBranch> FsBranches(params MountBranch[] branches)
        => ListModule.OfSeq(branches);

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
