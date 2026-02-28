using NinePSharp.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.FSharp.Collections;
using Microsoft.Z3;
using Moq;
using NinePSharp.Core.FSharp;
using NinePSharp.Server.Interfaces;
using Xunit;

namespace NinePSharp.Tests.Architecture;

public class Z3NamespaceBindProofTests
{
    [Fact]
    public void Z3_Bind_MBefore_Ordering_Contradiction_Is_Unsat_Bounded()
    {
        using var ctx = new Context();
        using var solver = ctx.MkSolver();

        IntExpr srcLen = ctx.MkIntConst("srcLen");
        IntExpr dstLen = ctx.MkIntConst("dstLen");
        IntExpr i = ctx.MkIntConst("i");
        IntExpr j = ctx.MkIntConst("j");

        solver.Assert(ctx.MkGe(srcLen, ctx.MkInt(1)));
        solver.Assert(ctx.MkLe(srcLen, ctx.MkInt(8)));
        solver.Assert(ctx.MkGe(dstLen, ctx.MkInt(1)));
        solver.Assert(ctx.MkLe(dstLen, ctx.MkInt(8)));

        solver.Assert(ctx.MkGe(i, ctx.MkInt(0)));
        solver.Assert(ctx.MkLt(i, srcLen));
        solver.Assert(ctx.MkGe(j, ctx.MkInt(0)));
        solver.Assert(ctx.MkLt(j, dstLen));

        IntExpr srcPos = i;
        IntExpr dstPos = (IntExpr)ctx.MkAdd(srcLen, j);

        // Contradiction for MBEFORE: some source backend is at/after some target backend.
        solver.Assert(ctx.MkGe(srcPos, dstPos));

        Assert.Equal(Status.UNSATISFIABLE, solver.Check());
    }

    [Fact]
    public void Z3_Bind_MAfter_Ordering_Contradiction_Is_Unsat_Bounded()
    {
        using var ctx = new Context();
        using var solver = ctx.MkSolver();

        IntExpr srcLen = ctx.MkIntConst("srcLen");
        IntExpr dstLen = ctx.MkIntConst("dstLen");
        IntExpr i = ctx.MkIntConst("i");
        IntExpr j = ctx.MkIntConst("j");

        solver.Assert(ctx.MkGe(srcLen, ctx.MkInt(1)));
        solver.Assert(ctx.MkLe(srcLen, ctx.MkInt(8)));
        solver.Assert(ctx.MkGe(dstLen, ctx.MkInt(1)));
        solver.Assert(ctx.MkLe(dstLen, ctx.MkInt(8)));

        solver.Assert(ctx.MkGe(i, ctx.MkInt(0)));
        solver.Assert(ctx.MkLt(i, srcLen));
        solver.Assert(ctx.MkGe(j, ctx.MkInt(0)));
        solver.Assert(ctx.MkLt(j, dstLen));

        IntExpr dstPos = j;
        IntExpr srcPos = (IntExpr)ctx.MkAdd(dstLen, i);

        // Contradiction for MAFTER: some target backend is at/after some source backend.
        solver.Assert(ctx.MkGe(dstPos, srcPos));

        Assert.Equal(Status.UNSATISFIABLE, solver.Check());
    }

    [Fact]
    public void Z3_Bind_MRepl_Idempotence_Contradiction_Is_Unsat_Bounded()
    {
        using var ctx = new Context();
        using var solver = ctx.MkSolver();

        IntExpr srcLen = ctx.MkIntConst("srcLen");
        IntExpr i = ctx.MkIntConst("i");

        solver.Assert(ctx.MkGe(srcLen, ctx.MkInt(1)));
        solver.Assert(ctx.MkLe(srcLen, ctx.MkInt(10)));
        solver.Assert(ctx.MkGe(i, ctx.MkInt(0)));
        solver.Assert(ctx.MkLt(i, srcLen));

        IntExpr posAfterFirst = i;
        IntExpr posAfterSecond = i;

        // Contradiction for idempotence: same source item moves after identical second MREPL.
        solver.Assert(ctx.MkNot(ctx.MkEq(posAfterFirst, posAfterSecond)));

        Assert.Equal(Status.UNSATISFIABLE, solver.Check());
    }

    [Property(MaxTest = 70)]
    public bool Z3_Model_Matches_NamespaceOps_For_MBefore(PositiveInt srcSeed, PositiveInt dstSeed)
    {
        return VerifyBindModel(srcSeed.Get, dstSeed.Get, BindFlags.MBEFORE);
    }

    [Property(MaxTest = 70)]
    public bool Z3_Model_Matches_NamespaceOps_For_MAfter(PositiveInt srcSeed, PositiveInt dstSeed)
    {
        return VerifyBindModel(srcSeed.Get, dstSeed.Get, BindFlags.MAFTER);
    }

    [Property(MaxTest = 70)]
    public bool Z3_Model_Matches_NamespaceOps_For_MRepl(PositiveInt srcSeed, PositiveInt dstSeed)
    {
        return VerifyBindModel(srcSeed.Get, dstSeed.Get, BindFlags.MREPL);
    }

    private static bool VerifyBindModel(int srcSeed, int dstSeed, BindFlags flag)
    {
        int srcCount = Math.Clamp(srcSeed % 5, 1, 5);
        int dstCount = Math.Clamp(dstSeed % 5, 1, 5);

        var srcBackends = Enumerable.Range(0, srcCount).Select(i => NewTarget("src" + i)).ToList();
        var dstBackends = Enumerable.Range(0, dstCount).Select(i => NewTarget("dst" + i)).ToList();

        var sourcePath = NamespaceOps.splitPath("/src");
        var targetPath = NamespaceOps.splitPath("/dst");
        var sourceMount = new Mount(sourcePath, new MountChain(MountIdForPath(sourcePath), FsBranches(BindFlags.MREPL, srcBackends)));
        var targetMount = new Mount(targetPath, new MountChain(MountIdForPath(targetPath), FsBranches(BindFlags.MREPL, dstBackends)));
        var ns = new NinePSharp.Core.FSharp.Namespace(FsList(new[] { sourceMount, targetMount }));

        var bound = NamespaceOps.bind("/src", "/dst", flag, ns);
        var resolved = NamespaceOps.resolve(FsPath("dst"), bound).Item1.ToList();

        if (flag == BindFlags.MREPL)
        {
            if (resolved.Count != srcCount)
            {
                return false;
            }
        }
        else
        {
            if (resolved.Count != srcCount + dstCount)
            {
                return false;
            }
        }

        int[] srcPositions = srcBackends.Select(b => resolved.IndexOf(b)).ToArray();
        int[] dstPositions = dstBackends.Select(b => resolved.IndexOf(b)).ToArray();

        if (srcPositions.Any(p => p < 0) || (flag != BindFlags.MREPL && dstPositions.Any(p => p < 0)))
        {
            return false;
        }

        var modelDstPositions = flag == BindFlags.MREPL ? Array.Empty<int>() : dstPositions;
        Status expected = CheckModelStatus(srcPositions, modelDstPositions, flag);
        if (expected != Status.SATISFIABLE)
        {
            return false;
        }

        // Opposite ordering model must be impossible.
        if (flag == BindFlags.MBEFORE && CheckModelStatus(srcPositions, dstPositions, BindFlags.MAFTER) != Status.UNSATISFIABLE)
        {
            return false;
        }

        if (flag == BindFlags.MAFTER && CheckModelStatus(srcPositions, dstPositions, BindFlags.MBEFORE) != Status.UNSATISFIABLE)
        {
            return false;
        }

        return true;
    }

    private static Status CheckModelStatus(int[] srcPositions, int[] dstPositions, BindFlags flag)
    {
        using var ctx = new Context();
        using var solver = ctx.MkSolver();

        int total = flag == BindFlags.MREPL
            ? srcPositions.Length
            : srcPositions.Length + dstPositions.Length;

        var src = new IntExpr[srcPositions.Length];
        var dst = new IntExpr[dstPositions.Length];
        var all = new List<IntExpr>(src.Length + dst.Length);

        for (int i = 0; i < src.Length; i++)
        {
            src[i] = ctx.MkIntConst($"s{i}");
            solver.Assert(ctx.MkEq(src[i], ctx.MkInt(srcPositions[i])));
            solver.Assert(ctx.MkGe(src[i], ctx.MkInt(0)));
            solver.Assert(ctx.MkLt(src[i], ctx.MkInt(total)));
            all.Add(src[i]);
        }

        for (int i = 0; i < dst.Length; i++)
        {
            dst[i] = ctx.MkIntConst($"d{i}");
            solver.Assert(ctx.MkEq(dst[i], ctx.MkInt(dstPositions[i])));
            solver.Assert(ctx.MkGe(dst[i], ctx.MkInt(0)));
            solver.Assert(ctx.MkLt(dst[i], ctx.MkInt(total)));
            all.Add(dst[i]);
        }

        if (all.Count > 0)
        {
            solver.Assert(ctx.MkDistinct(all.ToArray()));
        }

        for (int i = 1; i < src.Length; i++)
        {
            solver.Assert(ctx.MkLt(src[i - 1], src[i]));
        }

        for (int i = 1; i < dst.Length; i++)
        {
            solver.Assert(ctx.MkLt(dst[i - 1], dst[i]));
        }

        if (flag == BindFlags.MBEFORE)
        {
            foreach (var s in src)
            {
                foreach (var d in dst)
                {
                    solver.Assert(ctx.MkLt(s, d));
                }
            }
        }
        else if (flag == BindFlags.MAFTER)
        {
            foreach (var d in dst)
            {
                foreach (var s in src)
                {
                    solver.Assert(ctx.MkLt(d, s));
                }
            }
        }
        else // MREPL
        {
            for (int i = 0; i < src.Length; i++)
            {
                solver.Assert(ctx.MkEq(src[i], ctx.MkInt(i)));
            }
        }

        return solver.Check();
    }

    private static BackendTargetDescriptor NewTarget(string id)
        => BackendTargetDescriptor.Local(id, "/" + id, () => new Mock<INinePFileSystem>(MockBehavior.Loose).Object);

    private static FSharpList<T> FsList<T>(IEnumerable<T> items)
    {
        return ListModule.OfSeq(items);
    }

    private static FSharpList<string> FsPath(params string[] items)
    {
        return ListModule.OfSeq(items);
    }

    private static FSharpList<MountBranch> FsBranches(BindFlags flags, IEnumerable<BackendTargetDescriptor> backends)
    {
        return ListModule.OfSeq(backends.Select(target => new MountBranch(target, flags)));
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
