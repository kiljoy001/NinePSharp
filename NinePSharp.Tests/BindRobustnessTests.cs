using NinePSharp.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FsCheck.Xunit;
using Microsoft.Coyote.Specifications;
using Microsoft.Coyote.SystematicTesting;
using Microsoft.FSharp.Collections;
using Moq;
using NinePSharp.Core.FSharp;
using NinePSharp.Server.Interfaces;
using Xunit;
using CoyoteTask = Microsoft.Coyote.Rewriting.Types.Threading.Tasks.Task;

namespace NinePSharp.Tests.Architecture;

public class BindRobustnessTests
{
    private static Channel RootChannel => ChannelOps.createNamespaceNode(new NinePSharp.Core.FSharp.Qid(QidType.QTDIR, 0, 0), FsList<string>(Array.Empty<string>()));

    [Property(MaxTest = 120)]
    public bool Bind_MREPL_Is_Idempotent(string sourceRaw, string targetRaw)
    {
        var source = CleanPathSegment(sourceRaw, "src");
        var target = CleanPathSegment(targetRaw, "dst");
        if (source == target) target += "_t";

        var srcFs = NewTarget("src");
        var dstFs = NewTarget("dst");
        var initial = BuildNamespace(
            MountAt("/" + source, srcFs),
            MountAt("/" + target, dstFs));

        var ns1 = NamespaceOps.bind("/" + source, "/" + target, BindFlags.MREPL, initial);
        var ns2 = NamespaceOps.bind("/" + source, "/" + target, BindFlags.MREPL, ns1);

        // With 9front exact-match semantics, resolve the exact mount path (normalized)
        var targetPath = NamespaceOps.splitPath("/" + target);
        var r1 = NamespaceOps.resolve(targetPath, ns1).Item1.ToList();
        var r2 = NamespaceOps.resolve(targetPath, ns2).Item1.ToList();

        return r1.Count == 1 && r2.Count == 1 && ReferenceEquals(r1[0], r2[0]) && ReferenceEquals(r1[0], srcFs);
    }

    [Property(MaxTest = 100)]
    public bool Bind_Ordering_Preserves_Search_Priority(string[] sourcesRaw, string targetRaw)
    {
        if (sourcesRaw == null) return true;

        var target = CleanPathSegment(targetRaw, "target");
        var sources = sourcesRaw
            .Select((s, i) => CleanPathSegment(s, "src" + i))
            .Distinct(StringComparer.Ordinal)
            .Where(s => !string.Equals(s, target, StringComparison.Ordinal))
            .Take(5)
            .ToList();
        if (sources.Count == 0) return true;

        var targetFs = NewTarget("target");
        var mounts = new List<MountChain> { MountAt("/" + target, targetFs) };
        var sourceBackends = new Dictionary<string, BackendTargetDescriptor>(StringComparer.Ordinal);

        foreach (var source in sources)
        {
            var fs = NewTarget(source);
            mounts.Add(MountAt("/" + source, fs));
            sourceBackends[source] = fs;
        }

        var ns = BuildNamespace(mounts.ToArray());
        foreach (var source in sources)
        {
            ns = NamespaceOps.bind("/" + source, "/" + target, BindFlags.MAFTER, ns);
        }

        // With 9front exact-match semantics, resolve the exact mount path (normalized)
        var targetPath = NamespaceOps.splitPath("/" + target);
        var resolved = NamespaceOps.resolve(targetPath, ns).Item1.ToList();
        if (resolved.Count != sources.Count + 1) return false;
        if (!ReferenceEquals(resolved[0], targetFs)) return false;

        for (var i = 0; i < sources.Count; i++)
        {
            if (!ReferenceEquals(resolved[i + 1], sourceBackends[sources[i]]))
            {
                return false;
            }
        }

        return true;
    }

    [Property(MaxTest = 80)]
    public bool Bind_Cycles_Should_Be_Detected_Or_Safe(string pathARaw, string pathBRaw)
    {
        var pathA = CleanPathSegment(pathARaw, "a");
        var pathB = CleanPathSegment(pathBRaw, "b");
        if (pathA == pathB) pathB += "_b";

        var fsA = NewTarget("a");
        var fsB = NewTarget("b");
        var ns = BuildNamespace(
            MountAt("/" + pathA, fsA),
            MountAt("/" + pathB, fsB));

        // a -> b, b -> a
        var ns2 = NamespaceOps.bind("/" + pathA, "/" + pathB, BindFlags.MREPL, ns);
        var ns3 = NamespaceOps.bind("/" + pathB, "/" + pathA, BindFlags.MREPL, ns2);

        // With 9front exact-match semantics, resolve the exact mount path (normalized)
        var pathANorm = NamespaceOps.splitPath("/" + pathA);
        var pathBNorm = NamespaceOps.splitPath("/" + pathB);
        var rA = NamespaceOps.resolve(pathANorm, ns3).Item1.ToList();
        var rB = NamespaceOps.resolve(pathBNorm, ns3).Item1.ToList();

        return rA.Count > 0 && rB.Count > 0;
    }

    [Fact]
    public void TestBindConcurrencyWithCoyote()
    {
        var mountA = NewTarget("a");
        var mountB = NewTarget("b");
        var mountBin = NewTarget("bin");

        var baseNs = BuildNamespace(
            MountAt("/a", mountA),
            MountAt("/b", mountB),
            MountAt("/bin", mountBin));

        var parent = Process.create(1, baseNs, RootChannel);

        var c1 = CoyoteTask.Run(() =>
        {
            var child = Process.fork(2, parent);
            return CoyoteTask.FromResult(Process.bind("/a", "/bin", BindFlags.MREPL, child));
        });

        var c2 = CoyoteTask.Run(() =>
        {
            var child = Process.fork(3, parent);
            return CoyoteTask.FromResult(Process.bind("/b", "/bin", BindFlags.MREPL, child));
        });

        CoyoteTask.WaitAll(c1, c2);

        // With 9front exact-match semantics, resolve the exact mount path (normalized)
        var binPath = NamespaceOps.splitPath("/bin");
        var res1 = NamespaceOps.resolve(binPath, c1.Result.Namespace).Item1.ToList();
        var res2 = NamespaceOps.resolve(binPath, c2.Result.Namespace).Item1.ToList();

        Specification.Assert(res1.Count == 1, "Child 1 should have 1 backend at /bin");
        Specification.Assert(ReferenceEquals(res1[0], mountA), "Child 1 should see /a at /bin");
        Specification.Assert(res2.Count == 1, "Child 2 should have 1 backend at /bin");
        Specification.Assert(ReferenceEquals(res2[0], mountB), "Child 2 should see /b at /bin");
    }

    private static NinePSharp.Core.FSharp.Namespace EmptyNamespace()
        => NamespaceOps.empty;

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
        => ListModule.OfSeq(items);

    private static FSharpList<T> FsList<T>(params T[] items) => FsList((IEnumerable<T>)items);

    private static FSharpList<MountBranch> FsBranches(BindFlags flags, params BackendTargetDescriptor[] targets)
        => FsList(targets.Select(t => new MountBranch(t, flags)));

    private static string CleanPathSegment(string raw, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        var clean = raw.Replace("/", "").Trim();
        return string.IsNullOrWhiteSpace(clean) ? fallback : clean;
    }

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
