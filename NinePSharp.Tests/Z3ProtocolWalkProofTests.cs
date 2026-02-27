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

public class Z3ProtocolWalkProofTests
{
    [Fact]
    public void Z3_Walk_Depth_Never_Goes_Negative_For_Bounded_Traces()
    {
        const int steps = 8;

        using var ctx = new Context();
        using var solver = ctx.MkSolver();

        IntExpr d0 = ctx.MkIntConst("d0");
        solver.Assert(ctx.MkGe(d0, ctx.MkInt(0)));
        solver.Assert(ctx.MkLe(d0, ctx.MkInt(steps)));

        var depths = new List<IntExpr> { d0 };

        for (int i = 0; i < steps; i++)
        {
            IntExpr segment = ctx.MkIntConst($"s{i}");
            solver.Assert(ctx.MkOr(
                ctx.MkEq(segment, ctx.MkInt(-1)), // ".."
                ctx.MkEq(segment, ctx.MkInt(0)),  // "."
                ctx.MkEq(segment, ctx.MkInt(1)))); // normal component

            depths.Add(ApplySegmentExpr(ctx, depths[i], segment));
        }

        // Negated invariant: some intermediate depth is negative.
        BoolExpr negatedInvariant = ctx.MkOr(depths.Select(d => ctx.MkLt(d, ctx.MkInt(0))).ToArray());
        solver.Assert(negatedInvariant);

        Assert.Equal(Status.UNSATISFIABLE, solver.Check());
    }

    [Fact]
    public void Z3_NoStickyDelegation_Result_Is_Function_Of_Inputs()
    {
        const int steps = 8;

        using var ctx = new Context();
        using var solver = ctx.MkSolver();

        IntExpr d0 = ctx.MkIntConst("d0");
        solver.Assert(ctx.MkGe(d0, ctx.MkInt(0)));
        solver.Assert(ctx.MkLe(d0, ctx.MkInt(steps)));

        IntExpr left = d0;
        IntExpr right = d0;

        for (int i = 0; i < steps; i++)
        {
            IntExpr segment = ctx.MkIntConst($"s{i}");
            solver.Assert(ctx.MkOr(
                ctx.MkEq(segment, ctx.MkInt(-1)),
                ctx.MkEq(segment, ctx.MkInt(0)),
                ctx.MkEq(segment, ctx.MkInt(1))));

            left = ApplySegmentExpr(ctx, left, segment);
            right = ApplySegmentExpr(ctx, right, segment);
        }

        // Negated property: equal inputs lead to different outputs.
        solver.Assert(ctx.MkNot(ctx.MkEq(left, right)));

        Assert.Equal(Status.UNSATISFIABLE, solver.Check());
    }

    [Property(MaxTest = 60)]
    public bool Z3_Model_Matches_ChannelOps_Walk_For_Fuzzed_Segments(string[] rawSegments, NonNegativeInt depthSeed)
    {
        if (rawSegments == null)
        {
            return true;
        }

        int initialDepth = depthSeed.Get % 12;
        var initialPath = Enumerable.Range(0, initialDepth).Select(i => $"p{i}");

        var fs = new Mock<INinePFileSystem>(MockBehavior.Loose).Object;
        var channel = new Channel(
            new NinePSharp.Core.FSharp.Qid(QidType.QTDIR, 0, 1),
            0UL,
            fs,
            FsList(initialPath),
            false);

        var segments = rawSegments
            .Select(NormalizeSegment)
            .Take(24)
            .ToList();

        var walked = ChannelOps.walk(FsList(segments), channel);
        int implementationDepth = walked.InternalPath.Length;

        using var ctx = new Context();
        using var solver = ctx.MkSolver();

        IntExpr depth = ctx.MkInt(initialDepth);
        foreach (var segment in segments)
        {
            depth = ApplySegmentExpr(ctx, depth, ctx.MkInt(ToSegmentCode(segment)));
        }

        // If model and implementation diverge, SAT; if they agree, UNSAT.
        solver.Assert(ctx.MkNot(ctx.MkEq(depth, ctx.MkInt(implementationDepth))));
        return solver.Check() == Status.UNSATISFIABLE;
    }

    private static IntExpr ApplySegmentExpr(Context ctx, IntExpr depth, IntExpr segment)
    {
        var isUp = ctx.MkEq(segment, ctx.MkInt(-1));
        var isStay = ctx.MkEq(segment, ctx.MkInt(0));

        var decrement = (IntExpr)ctx.MkSub(depth, ctx.MkInt(1));
        var clampedDecrement = (IntExpr)ctx.MkITE(ctx.MkGt(depth, ctx.MkInt(0)), decrement, ctx.MkInt(0));
        var increment = (IntExpr)ctx.MkAdd(depth, ctx.MkInt(1));

        return (IntExpr)ctx.MkITE(
            isUp,
            clampedDecrement,
            ctx.MkITE(isStay, depth, increment));
    }

    private static string NormalizeSegment(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return ".";
        }

        return raw switch
        {
            ".." => "..",
            "." => ".",
            _ => "x"
        };
    }

    private static int ToSegmentCode(string segment)
    {
        return segment switch
        {
            ".." => -1,
            "." => 0,
            _ => 1
        };
    }

    private static FSharpList<T> FsList<T>(IEnumerable<T> items)
    {
        return ListModule.OfSeq(items);
    }
}
