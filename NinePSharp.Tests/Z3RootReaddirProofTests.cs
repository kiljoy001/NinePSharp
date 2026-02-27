using NinePSharp.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Z3;
using NinePSharp.Messages;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;
using NinePSharp.Tests.Helpers;
using Xunit;

namespace NinePSharp.Tests.Architecture;

public class Z3RootReaddirProofTests
{
    private const int FixedEntrySizeBytes = 28; // qid(13) + offset(8) + type(1) + nameLen(2) + name(4)

    [Fact]
    public void Z3_Readdir_FirstReturnedOffset_Must_Exceed_RequestOffset_Bounded()
    {
        using var ctx = new Context();
        using var solver = ctx.MkSolver();

        IntExpr requestOffset = ctx.MkIntConst("requestOffset");
        IntExpr firstReturnedOffset = ctx.MkIntConst("firstReturnedOffset");

        solver.Assert(ctx.MkGe(requestOffset, ctx.MkInt(0)));
        solver.Assert(ctx.MkLe(requestOffset, ctx.MkInt(63)));

        // Readdir contract for contiguous offsets: first returned entry must start at request+1.
        solver.Assert(ctx.MkEq(firstReturnedOffset, ctx.MkAdd(requestOffset, ctx.MkInt(1))));

        // Negated property.
        solver.Assert(ctx.MkLe(firstReturnedOffset, requestOffset));

        Assert.Equal(Status.UNSATISFIABLE, solver.Check());
    }

    [Property(MaxTest = 45)]
    public bool Z3_Model_Matches_RootReaddir_Pagination_For_FixedWidthMounts(PositiveInt backendSeed, PositiveInt entriesPerPageSeed)
    {
        int backendCount = Math.Clamp((backendSeed.Get % 72) + 8, 8, 80);
        int entriesPerPage = Math.Clamp((entriesPerPageSeed.Get % 8) + 1, 1, 8);
        uint pageBytes = (uint)(entriesPerPage * FixedEntrySizeBytes);

        var backends = Enumerable.Range(0, backendCount)
            .Select(i => (IProtocolBackend)new StubBackend(
                mountPath: "/" + i.ToString("0000"),
                factory: () => new MarkerFileSystem("m" + i.ToString("0000"))))
            .ToList();

        var rootFs = new RootFileSystem(backends, clusterManager: null);
        var observedOffsets = new List<int>();
        ulong requestOffset = 0;

        for (int page = 0; page < backendCount + 4; page++)
        {
            var response = rootFs.ReaddirAsync(new Treaddir(
                    size: 0,
                    tag: (ushort)(page + 1),
                    fid: 1,
                    offset: requestOffset,
                    count: pageBytes))
                .GetAwaiter()
                .GetResult();

            var entries = DispatcherIntegrationTestKit.ParseReaddirEntries(response.Data.Span);
            if (entries.Count == 0)
            {
                break;
            }

            var offsets = entries.Select(e => (int)e.NextOffset).ToArray();
            if (!ProvePageOffsets(requestOffset: (int)requestOffset, totalEntries: backendCount, offsets))
            {
                return false;
            }

            if (!ProvePageCardinality(
                    requestOffset: (int)requestOffset,
                    totalEntries: backendCount,
                    entriesPerPage: entriesPerPage,
                    actualCount: entries.Count))
            {
                return false;
            }

            observedOffsets.AddRange(offsets);
            requestOffset = entries[^1].NextOffset;
        }

        return ProveFullEnumeration(observedOffsets, backendCount);
    }

    private static bool ProvePageOffsets(int requestOffset, int totalEntries, IReadOnlyList<int> offsets)
    {
        using var ctx = new Context();
        using var solver = ctx.MkSolver();

        IntExpr req = ctx.MkIntConst("req");
        solver.Assert(ctx.MkEq(req, ctx.MkInt(requestOffset)));

        var invariants = new List<BoolExpr>();
        IntExpr? previous = null;

        for (int i = 0; i < offsets.Count; i++)
        {
            IntExpr oi = ctx.MkIntConst($"o{i}");
            solver.Assert(ctx.MkEq(oi, ctx.MkInt(offsets[i])));

            invariants.Add(ctx.MkGt(oi, req));
            invariants.Add(ctx.MkLe(oi, ctx.MkInt(totalEntries)));

            if (previous == null)
            {
                invariants.Add(ctx.MkEq(oi, ctx.MkAdd(req, ctx.MkInt(1))));
            }
            else
            {
                invariants.Add(ctx.MkEq(oi, ctx.MkAdd(previous, ctx.MkInt(1))));
            }

            previous = oi;
        }

        BoolExpr invariant = invariants.Count == 0
            ? ctx.MkTrue()
            : ctx.MkAnd(invariants.ToArray());

        solver.Assert(ctx.MkNot(invariant));
        return solver.Check() == Status.UNSATISFIABLE;
    }

    private static bool ProvePageCardinality(int requestOffset, int totalEntries, int entriesPerPage, int actualCount)
    {
        using var ctx = new Context();
        using var solver = ctx.MkSolver();

        IntExpr off = ctx.MkIntConst("off");
        IntExpr n = ctx.MkIntConst("n");
        IntExpr per = ctx.MkIntConst("per");
        IntExpr actual = ctx.MkIntConst("actual");

        solver.Assert(ctx.MkEq(off, ctx.MkInt(requestOffset)));
        solver.Assert(ctx.MkEq(n, ctx.MkInt(totalEntries)));
        solver.Assert(ctx.MkEq(per, ctx.MkInt(entriesPerPage)));
        solver.Assert(ctx.MkEq(actual, ctx.MkInt(actualCount)));

        IntExpr remaining = (IntExpr)ctx.MkSub(n, off);
        IntExpr clampedRemaining = (IntExpr)ctx.MkITE(
            ctx.MkGt(remaining, ctx.MkInt(0)),
            remaining,
            ctx.MkInt(0));

        IntExpr expected = (IntExpr)ctx.MkITE(
            ctx.MkLe(clampedRemaining, per),
            clampedRemaining,
            per);

        // Negated property: implementation page length differs from the model.
        solver.Assert(ctx.MkNot(ctx.MkEq(actual, expected)));
        return solver.Check() == Status.UNSATISFIABLE;
    }

    private static bool ProveFullEnumeration(IReadOnlyList<int> observedOffsets, int totalEntries)
    {
        using var ctx = new Context();
        using var solver = ctx.MkSolver();

        IntExpr count = ctx.MkIntConst("count");
        solver.Assert(ctx.MkEq(count, ctx.MkInt(observedOffsets.Count)));

        var invariants = new List<BoolExpr>
        {
            ctx.MkEq(count, ctx.MkInt(totalEntries))
        };

        var vars = new List<IntExpr>(observedOffsets.Count);
        for (int i = 0; i < observedOffsets.Count; i++)
        {
            IntExpr oi = ctx.MkIntConst($"all{i}");
            solver.Assert(ctx.MkEq(oi, ctx.MkInt(observedOffsets[i])));
            vars.Add(oi);

            // Expected complete ordering: 1,2,3,...,N
            invariants.Add(ctx.MkEq(oi, ctx.MkInt(i + 1)));
        }

        if (vars.Count > 0)
        {
            invariants.Add(ctx.MkDistinct(vars.ToArray()));
        }

        BoolExpr invariant = ctx.MkAnd(invariants.ToArray());
        solver.Assert(ctx.MkNot(invariant));

        return solver.Check() == Status.UNSATISFIABLE;
    }
}
