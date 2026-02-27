using NinePSharp.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Z3;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;
using NinePSharp.Tests.Helpers;
using Xunit;

namespace NinePSharp.Tests.Architecture;

public class Z3RootReadProofTests
{
    [Fact]
    public void Z3_RootRead_Count_Below_EntrySize_Returns_Empty()
    {
        var rootFs = BuildRootFs(12);
        int entrySize = DetectEntrySize(rootFs);

        var response = rootFs.ReadAsync(new Tread(tag: 1, fid: 0, offset: 0, count: (uint)(entrySize - 1))).Sync();

        using var ctx = new Context();
        using var solver = ctx.MkSolver();

        IntExpr length = ctx.MkIntConst("length");
        solver.Assert(ctx.MkEq(length, ctx.MkInt(response.Data.Length)));

        // Negated property: partial entry payload should still be returned.
        solver.Assert(ctx.MkGt(length, ctx.MkInt(0)));

        Assert.Equal(Status.UNSATISFIABLE, solver.Check());
    }

    [Fact]
    public void Z3_RootRead_Eof_Offset_Returns_Empty()
    {
        var rootFs = BuildRootFs(24);
        int entrySize = DetectEntrySize(rootFs);
        int totalBytes = 24 * entrySize;

        var response = rootFs.ReadAsync(new Tread(tag: 2, fid: 0, offset: (ulong)totalBytes, count: 4096)).Sync();

        using var ctx = new Context();
        using var solver = ctx.MkSolver();

        IntExpr length = ctx.MkIntConst("length");
        solver.Assert(ctx.MkEq(length, ctx.MkInt(response.Data.Length)));

        // Negated property: EOF offset returns non-empty payload.
        solver.Assert(ctx.MkGt(length, ctx.MkInt(0)));

        Assert.Equal(Status.UNSATISFIABLE, solver.Check());
    }

    [Property(MaxTest = 45)]
    public bool Z3_Model_Matches_RootRead_Pagination_For_FixedWidthStats(PositiveInt backendSeed, PositiveInt entriesPerPageSeed)
    {
        int backendCount = Math.Clamp((backendSeed.Get % 72) + 8, 8, 80);
        int entriesPerPage = Math.Clamp((entriesPerPageSeed.Get % 8) + 1, 1, 8);
        return EvaluateScenario(backendCount, entriesPerPage, out _);
    }

    [Fact]
    public void Z3_RootRead_Pagination_Repro_9_2()
    {
        bool ok = EvaluateScenario(backendCount: 9, entriesPerPage: 2, out string? reason);
        Assert.True(ok, reason);
    }

    private static bool EvaluateScenario(int backendCount, int entriesPerPage, out string? reason)
    {
        reason = null;

        var rootFs = BuildRootFs(backendCount);
        int entrySize = DetectEntrySize(rootFs);
        int totalBytes = backendCount * entrySize;
        uint pageBytes = (uint)(entriesPerPage * entrySize);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        int offsetBytes = 0;

        for (int step = 0; step < (backendCount / entriesPerPage) + 4; step++)
        {
            var response = rootFs.ReadAsync(new Tread(
                    tag: (ushort)(step + 3),
                    fid: 0,
                    offset: (ulong)offsetBytes,
                    count: pageBytes))
                .GetAwaiter()
                .GetResult();

            var stats = DispatcherIntegrationTestKit.ParseStatsTable(response.Data.Span);
            int actualEntries = stats.Count;

            if (!ProvePageCardinalityAndBytes(
                    totalBytes: totalBytes,
                    requestOffsetBytes: offsetBytes,
                    entriesPerPage: entriesPerPage,
                    entrySize: entrySize,
                    actualEntries: actualEntries,
                    actualBytes: response.Data.Length))
            {
                reason = $"cardinality mismatch step={step}, offset={offsetBytes}, actualEntries={actualEntries}, actualBytes={response.Data.Length}, entrySize={entrySize}, pageBytes={pageBytes}";
                return false;
            }

            if (!ProvePayloadAlignedToEntrySize(response.Data.Length, entrySize))
            {
                reason = $"alignment mismatch step={step}, payload={response.Data.Length}, entrySize={entrySize}";
                return false;
            }

            if (actualEntries == 0)
            {
                break;
            }

            foreach (var stat in stats)
            {
                seen.Add(stat.Name);
            }

            offsetBytes += response.Data.Length;
        }

        if (seen.Count != backendCount)
        {
            reason = $"seen mismatch seen={seen.Count}, expected={backendCount}, entrySize={entrySize}, pageBytes={pageBytes}";
            return false;
        }

        return true;
    }

    private static bool ProvePageCardinalityAndBytes(
        int totalBytes,
        int requestOffsetBytes,
        int entriesPerPage,
        int entrySize,
        int actualEntries,
        int actualBytes)
    {
        using var ctx = new Context();
        using var solver = ctx.MkSolver();

        IntExpr total = ctx.MkIntConst("total");
        IntExpr off = ctx.MkIntConst("off");
        IntExpr p = ctx.MkIntConst("p");
        IntExpr es = ctx.MkIntConst("es");
        IntExpr ae = ctx.MkIntConst("ae");
        IntExpr ab = ctx.MkIntConst("ab");

        solver.Assert(ctx.MkEq(total, ctx.MkInt(totalBytes)));
        solver.Assert(ctx.MkEq(off, ctx.MkInt(requestOffsetBytes)));
        solver.Assert(ctx.MkEq(p, ctx.MkInt(entriesPerPage)));
        solver.Assert(ctx.MkEq(es, ctx.MkInt(entrySize)));
        solver.Assert(ctx.MkEq(ae, ctx.MkInt(actualEntries)));
        solver.Assert(ctx.MkEq(ab, ctx.MkInt(actualBytes)));

        IntExpr remainingBytes = (IntExpr)ctx.MkSub(total, off);
        IntExpr clampedRemainingBytes = (IntExpr)ctx.MkITE(
            ctx.MkGt(remainingBytes, ctx.MkInt(0)),
            remainingBytes,
            ctx.MkInt(0));

        IntExpr availableEntries = (IntExpr)ctx.MkDiv(clampedRemainingBytes, es);
        IntExpr expectedEntries = (IntExpr)ctx.MkITE(ctx.MkLe(availableEntries, p), availableEntries, p);

        IntExpr expectedBytes = (IntExpr)ctx.MkMul(expectedEntries, es);
        BoolExpr invariant = ctx.MkAnd(
            ctx.MkEq(ae, expectedEntries),
            ctx.MkEq(ab, expectedBytes));

        // Negated property: implementation diverges from pagination model.
        solver.Assert(ctx.MkNot(invariant));
        return solver.Check() == Status.UNSATISFIABLE;
    }

    private static bool ProvePayloadAlignedToEntrySize(int payloadBytes, int entrySize)
    {
        using var ctx = new Context();
        using var solver = ctx.MkSolver();

        IntExpr bytes = ctx.MkIntConst("bytes");
        IntExpr size = ctx.MkIntConst("size");

        solver.Assert(ctx.MkEq(bytes, ctx.MkInt(payloadBytes)));
        solver.Assert(ctx.MkEq(size, ctx.MkInt(entrySize)));
        solver.Assert(ctx.MkGt(size, ctx.MkInt(0)));

        BoolExpr aligned = ctx.MkEq(ctx.MkMod(bytes, size), ctx.MkInt(0));

        // Negated property: payload is not an integer number of stat entries.
        solver.Assert(ctx.MkNot(aligned));
        return solver.Check() == Status.UNSATISFIABLE;
    }

    private static RootFileSystem BuildRootFs(int backendCount)
    {
        var backends = Enumerable.Range(0, backendCount)
            .Select(i => (IProtocolBackend)new StubBackend(
                mountPath: "/" + i.ToString("0000"),
                factory: () => new MarkerFileSystem("m" + i.ToString("0000"))))
            .ToList();

        return new RootFileSystem(backends, clusterManager: null);
    }

    private static int DetectEntrySize(RootFileSystem rootFs)
    {
        var full = rootFs.ReadAsync(new Tread(tag: 99, fid: 0, offset: 0, count: 65535))
            .GetAwaiter()
            .GetResult();

        if (full.Data.Length < 2)
        {
            throw new InvalidOperationException("Root read did not produce any stat entries.");
        }

        int offset = 0;
        _ = new Stat(full.Data.Span, ref offset);
        return offset;
    }
}
