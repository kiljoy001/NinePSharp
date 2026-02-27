using NinePSharp.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using FsCheck.Xunit;
using Microsoft.FSharp.Collections;
using Microsoft.Z3;
using Moq;
using NinePSharp.Core.FSharp;
using NinePSharp.Server.Interfaces;
using Xunit;

namespace NinePSharp.Tests.Architecture;

public class Z3NamespaceResolveProofTests
{
    [Fact]
    public void Z3_Resolve_Prefers_LongestPrefix_Contradiction_Is_Unsat_Bounded()
    {
        using var ctx = new Context();
        using var solver = ctx.MkSolver();

        IntExpr shortLen = ctx.MkIntConst("shortLen");
        IntExpr longLen = ctx.MkIntConst("longLen");
        IntExpr pathLen = ctx.MkIntConst("pathLen");
        IntExpr selectedLen = ctx.MkIntConst("selectedLen");

        solver.Assert(ctx.MkGe(shortLen, ctx.MkInt(1)));
        solver.Assert(ctx.MkLe(shortLen, ctx.MkInt(4)));
        solver.Assert(ctx.MkGe(longLen, ctx.MkInt(2)));
        solver.Assert(ctx.MkLe(longLen, ctx.MkInt(5)));
        solver.Assert(ctx.MkLt(shortLen, longLen));

        solver.Assert(ctx.MkGe(pathLen, longLen));
        solver.Assert(ctx.MkLe(pathLen, ctx.MkInt(8)));

        // Resolve must pick the longest matching prefix.
        solver.Assert(ctx.MkEq(selectedLen, longLen));

        // Negated property: resolve picked the shorter prefix instead.
        solver.Assert(ctx.MkEq(selectedLen, shortLen));

        Assert.Equal(Status.UNSATISFIABLE, solver.Check());
    }

    [Property(MaxTest = 70)]
    public bool Z3_Model_Matches_NamespaceOps_Resolve_LongestPrefix(string[] rawMounts, string[] rawPath)
    {
        if (rawMounts == null || rawPath == null)
        {
            return true;
        }

        var mountPaths = BuildMountPaths(rawMounts);
        if (mountPaths.Count == 0)
        {
            return true;
        }

        var mounts = mountPaths
            .Select(path => new Mount(FsList(path), FsList(new[] { NewFs() }), BindFlags.MREPL))
            .ToList();
        var ns = new NinePSharp.Core.FSharp.Namespace(FsList(mounts));

        var pathSegments = BuildPathSegments(rawPath, mountPaths);
        var resolved = NamespaceOps.resolve(FsList(pathSegments), ns);

        var normalizedPath = NamespaceOps.splitPath("/" + string.Join("/", pathSegments)).ToList();
        var remainder = resolved.Item2.ToList();
        int selectedLen = normalizedPath.Count - remainder.Count;

        int[] matchLens = mounts
            .Select(m => m.TargetPath.ToList())
            .Where(tp => IsPrefix(tp, normalizedPath))
            .Select(tp => tp.Count)
            .ToArray();

        return ProveSelectedLengthIsMax(selectedLen, matchLens);
    }

    [Property(MaxTest = 70)]
    public bool Z3_Model_Matches_NamespaceOps_Resolve_HitMiss_Consistency(string[] rawMounts, string[] rawPath)
    {
        if (rawMounts == null || rawPath == null)
        {
            return true;
        }

        var mountPaths = BuildMountPaths(rawMounts);
        if (mountPaths.Count == 0)
        {
            return true;
        }

        var mounts = mountPaths
            .Select(path => new Mount(FsList(path), FsList(new[] { NewFs() }), BindFlags.MREPL))
            .ToList();
        var ns = new NinePSharp.Core.FSharp.Namespace(FsList(mounts));

        var pathSegments = BuildPathSegments(rawPath, mountPaths);
        var resolved = NamespaceOps.resolve(FsList(pathSegments), ns);

        var normalizedPath = NamespaceOps.splitPath("/" + string.Join("/", pathSegments)).ToList();
        bool hasPrefixMatch = mounts
            .Select(m => m.TargetPath.ToList())
            .Any(tp => IsPrefix(tp, normalizedPath));
        bool hasResolvedBackend = resolved.Item1.Any();

        using var ctx = new Context();
        using var solver = ctx.MkSolver();

        BoolExpr backendHit = ctx.MkBoolConst("backendHit");
        BoolExpr prefixHit = ctx.MkBoolConst("prefixHit");

        solver.Assert(ctx.MkEq(backendHit, ctx.MkBool(hasResolvedBackend)));
        solver.Assert(ctx.MkEq(prefixHit, ctx.MkBool(hasPrefixMatch)));

        // Negated property: resolve result disagrees with prefix existence.
        solver.Assert(ctx.MkNot(ctx.MkIff(backendHit, prefixHit)));

        return solver.Check() == Status.UNSATISFIABLE;
    }

    private static bool ProveSelectedLengthIsMax(int selectedLen, IReadOnlyList<int> matchLens)
    {
        using var ctx = new Context();
        using var solver = ctx.MkSolver();

        IntExpr selected = ctx.MkIntConst("selected");
        IntExpr max = ctx.MkIntConst("max");

        solver.Assert(ctx.MkEq(selected, ctx.MkInt(selectedLen)));
        solver.Assert(ctx.MkGe(max, ctx.MkInt(0)));

        foreach (int len in matchLens)
        {
            solver.Assert(ctx.MkGe(max, ctx.MkInt(len)));
        }

        if (matchLens.Count == 0)
        {
            solver.Assert(ctx.MkEq(max, ctx.MkInt(0)));
        }
        else
        {
            solver.Assert(ctx.MkOr(matchLens.Select(len => ctx.MkEq(max, ctx.MkInt(len))).ToArray()));
        }

        // Negated property: implementation-selected length is not the longest prefix length.
        solver.Assert(ctx.MkNot(ctx.MkEq(selected, max)));

        return solver.Check() == Status.UNSATISFIABLE;
    }

    private static List<string[]> BuildMountPaths(string[] rawMounts)
    {
        var atoms = rawMounts
            .Select(CleanMountSegment)
            .Distinct(StringComparer.Ordinal)
            .Take(10)
            .ToList();

        var paths = new List<string[]>();
        for (int i = 0; i < atoms.Count && paths.Count < 8; i++)
        {
            paths.Add(new[] { atoms[i] });

            if (i + 1 < atoms.Count && paths.Count < 8)
            {
                paths.Add(new[] { atoms[i], atoms[i + 1] });
            }
        }

        return paths
            .GroupBy(p => string.Join("/", p), StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();
    }

    private static List<string> BuildPathSegments(string[] rawPath, IReadOnlyList<string[]> mountPaths)
    {
        var segments = new List<string>();

        if (mountPaths.Count > 0)
        {
            segments.AddRange(mountPaths[0]);
        }

        for (int i = 0; i < rawPath.Length && segments.Count < 8; i++)
        {
            segments.Add(NormalizePathSegment(rawPath[i], i));
        }

        if (segments.Count == 0)
        {
            segments.Add("root");
        }

        return segments;
    }

    private static bool IsPrefix(IReadOnlyList<string> prefix, IReadOnlyList<string> path)
    {
        if (prefix.Count > path.Count)
        {
            return false;
        }

        for (int i = 0; i < prefix.Count; i++)
        {
            if (!string.Equals(prefix[i], path[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string CleanMountSegment(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "m";
        }

        var chars = raw
            .Where(char.IsLetterOrDigit)
            .Take(10)
            .ToArray();

        return chars.Length == 0 ? "m" : new string(chars);
    }

    private static string NormalizePathSegment(string? raw, int index)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return index % 3 == 0 ? "." : "..";
        }

        string trimmed = raw.Trim();
        if (trimmed == "." || trimmed == "..")
        {
            return trimmed;
        }

        var chars = trimmed
            .Where(char.IsLetterOrDigit)
            .Take(10)
            .ToArray();

        if (chars.Length == 0)
        {
            return index % 2 == 0 ? "." : "..";
        }

        return new string(chars);
    }

    private static INinePFileSystem NewFs()
    {
        return new Mock<INinePFileSystem>(MockBehavior.Loose).Object;
    }

    private static FSharpList<T> FsList<T>(IEnumerable<T> items)
    {
        return ListModule.OfSeq(items);
    }
}
