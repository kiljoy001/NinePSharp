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
    [Property(MaxTest = 120)]
    public bool Bind_MREPL_Is_Idempotent(string sourceRaw, string targetRaw)
    {
        var source = CleanPathSegment(sourceRaw, "src");
        var target = CleanPathSegment(targetRaw, "dst");
        if (source == target) target += "_t";

        var srcFs = NewFs();
        var dstFs = NewFs();
        var initial = BuildNamespace(
            MountAt("/" + source, srcFs),
            MountAt("/" + target, dstFs));

        var ns1 = NamespaceOps.bind("/" + source, "/" + target, BindFlags.MREPL, initial);
        var ns2 = NamespaceOps.bind("/" + source, "/" + target, BindFlags.MREPL, ns1);

        var r1 = NamespaceOps.resolve(FsList(target, "x"), ns1).Item1.ToList();
        var r2 = NamespaceOps.resolve(FsList(target, "x"), ns2).Item1.ToList();

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

        var targetFs = NewFs();
        var mounts = new List<Mount> { MountAt("/" + target, targetFs) };
        var sourceBackends = new Dictionary<string, INinePFileSystem>(StringComparer.Ordinal);

        foreach (var source in sources)
        {
            var fs = NewFs();
            mounts.Add(MountAt("/" + source, fs));
            sourceBackends[source] = fs;
        }

        var ns = BuildNamespace(mounts.ToArray());
        foreach (var source in sources)
        {
            ns = NamespaceOps.bind("/" + source, "/" + target, BindFlags.MAFTER, ns);
        }

        var resolved = NamespaceOps.resolve(FsList(target, "lookup"), ns).Item1.ToList();
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

        var fsA = NewFs();
        var fsB = NewFs();
        var ns = BuildNamespace(
            MountAt("/" + pathA, fsA),
            MountAt("/" + pathB, fsB));

        try
        {
            var ns1 = NamespaceOps.bind("/" + pathA, "/" + pathB, BindFlags.MAFTER, ns);
            var ns2 = NamespaceOps.bind("/" + pathB, "/" + pathA, BindFlags.MAFTER, ns1);

            var ra = NamespaceOps.resolve(FsList(pathA), ns2).Item1.ToList();
            var rb = NamespaceOps.resolve(FsList(pathB), ns2).Item1.ToList();

            return ra.Count > 0 && rb.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    [Fact]
    public void Coyote_Process_Namespace_Isolation_Test()
    {
        var configuration = Microsoft.Coyote.Configuration.Create()
            .WithTestingIterations(200)
            .WithPartiallyControlledConcurrencyAllowed(true);

        var engine = TestingEngine.Create(configuration, async () =>
        {
            var mountA = NewFs();
            var mountB = NewFs();
            var mountBin = NewFs();

            var baseNs = BuildNamespace(
                MountAt("/a", mountA),
                MountAt("/b", mountB),
                MountAt("/bin", mountBin));

            var parent = Process.create(1, baseNs);

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

            var children = await CoyoteTask.WhenAll(c1, c2);

            var parentBin = NamespaceOps.resolve(FsList("bin"), parent.Namespace).Item1.ToList();
            Specification.Assert(parentBin.Count == 1 && ReferenceEquals(parentBin[0], mountBin), "Parent namespace must not be mutated by child binds.");

            var c1Bin = NamespaceOps.resolve(FsList("bin"), children[0].Namespace).Item1.ToList();
            var c2Bin = NamespaceOps.resolve(FsList("bin"), children[1].Namespace).Item1.ToList();
            Specification.Assert(c1Bin.Count == 1 && ReferenceEquals(c1Bin[0], mountA), "Child 1 should see /bin rebound to /a.");
            Specification.Assert(c2Bin.Count == 1 && ReferenceEquals(c2Bin[0], mountB), "Child 2 should see /bin rebound to /b.");
        });

        engine.Run();
        Assert.True(engine.TestReport.NumOfFoundBugs == 0, engine.TestReport.GetText(configuration));
    }

    [Fact]
    public void Fuzz_Bind_Path_Traversal_Attacks()
    {
        var attacks = new[]
        {
            "../../etc/passwd",
            "/absolute/host/path",
            "\0\0\0",
            @"C:\windows\system32",
            "///////",
            " ",
            "././././a",
            "a/../../b"
        };

        var random = new Random(271828);
        var safe = NewFs();
        var baseNs = BuildNamespace(MountAt("/safe", safe));

        foreach (var attack in attacks.Concat(Enumerable.Range(0, 100).Select(_ => RandomPath(random))))
        {
            var parts = NamespaceOps.splitPath(attack).ToList();
            parts.Should().OnlyContain(p => p.Length > 0 && p != "." && p != "..");

            var bound = NamespaceOps.bind(attack, "/safe", BindFlags.MREPL, baseNs);
            var _ = NamespaceOps.resolve(FsList("safe"), bound);
        }
    }

    private static string RandomPath(Random random)
    {
        var segments = random.Next(1, 6);
        var parts = new List<string>(segments);
        for (var i = 0; i < segments; i++)
        {
            var choice = random.Next(0, 6);
            parts.Add(choice switch
            {
                0 => "..",
                1 => ".",
                2 => "",
                3 => "seg" + random.Next(0, 100),
                4 => "/" + random.Next(0, 100),
                _ => "x" + Guid.NewGuid().ToString("N")[..4]
            });
        }

        return string.Join("/", parts);
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
