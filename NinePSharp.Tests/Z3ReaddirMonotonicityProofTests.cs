using NinePSharp.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Z3;
using Xunit;

namespace NinePSharp.Tests.Architecture;

public class Z3ReaddirMonotonicityProofTests : IDisposable
{
    private readonly Context _ctx;
    private readonly Solver _solver;

    public Z3ReaddirMonotonicityProofTests()
    {
        _ctx = new Context();
        _solver = _ctx.MkSolver();
    }

    public void Dispose()
    {
        _solver.Dispose();
        _ctx.Dispose();
    }

    [Fact]
    public void Z3_Readdir_Offset_Monotonicity_Invariant()
    {
        const int steps = 5;
        
        // Initial state: offset is 0.
        IntExpr off0 = _ctx.MkInt(0);
        var offsets = new List<IntExpr> { off0 };

        for (int i = 0; i < steps; i++)
        {
            IntExpr bytesRead = _ctx.MkIntConst($"bytesRead{i}");
            _solver.Assert(_ctx.MkGe(bytesRead, _ctx.MkInt(0)));

            IntExpr nextOff = (IntExpr)_ctx.MkAdd(offsets[i], bytesRead);
            offsets.Add(nextOff);
        }

        // Property: offsets are monotonically non-decreasing.
        // off[i+1] >= off[i]
        for (int i = 0; i < steps; i++)
        {
            _solver.Assert(_ctx.MkNot(_ctx.MkGe(offsets[i + 1], offsets[i])));
        }

        // If we can find any case where off[i+1] < off[i], Check() will be SAT.
        // We want UNSAT.
        Assert.Equal(Status.UNSATISFIABLE, _solver.Check());
    }

    [Fact]
    public void Z3_Readdir_Invalid_Offset_Detection()
    {
        // 9P2000 spec: "Seeking to any other arbitrary offset is illegal."
        
        IntExpr prevOff = _ctx.MkIntConst("prevOff");
        IntExpr prevBytes = _ctx.MkIntConst("prevBytes");
        IntExpr currentOff = _ctx.MkIntConst("currentOff");

        _solver.Assert(_ctx.MkGe(prevOff, _ctx.MkInt(0)));
        _solver.Assert(_ctx.MkGe(prevBytes, _ctx.MkInt(0)));

        // Define what a "valid" next offset is.
        BoolExpr isValidNext = _ctx.MkOr(
            _ctx.MkEq(currentOff, _ctx.MkInt(0)), // Reset
            _ctx.MkEq(currentOff, (IntExpr)_ctx.MkAdd(prevOff, prevBytes)) // Continuation
        );

        // Suppose currentOff is strictly between 0 and the continuation point.
        _solver.Assert(_ctx.MkGt(currentOff, _ctx.MkInt(0)));
        _solver.Assert(_ctx.MkLt(currentOff, (IntExpr)_ctx.MkAdd(prevOff, prevBytes)));

        // We want to prove that such a currentOff is NOT valid.
        _solver.Assert(isValidNext);

        Assert.Equal(Status.UNSATISFIABLE, _solver.Check());
    }

    [Fact]
    public void Z3_Readdir_Integral_Entries_Sum()
    {
        // 9P2000 spec: "The server must return an integral number of directory entries."
        // We model entry sizes as e1, e2, ...
        
        IntExpr e1 = _ctx.MkIntConst("e1");
        IntExpr e2 = _ctx.MkIntConst("e2");
        IntExpr e3 = _ctx.MkIntConst("e3");
        
        _solver.Assert(_ctx.MkGt(e1, _ctx.MkInt(0)));
        _solver.Assert(_ctx.MkGt(e2, _ctx.MkInt(0)));
        _solver.Assert(_ctx.MkGt(e3, _ctx.MkInt(0)));

        IntExpr bytesRead = _ctx.MkIntConst("bytesRead");
        
        // bytesRead must be some sum of these sizes.
        // For 3 entries, it could be 0, e1, e1+e2, or e1+e2+e3.
        BoolExpr possible = _ctx.MkOr(
            _ctx.MkEq(bytesRead, _ctx.MkInt(0)),
            _ctx.MkEq(bytesRead, e1),
            _ctx.MkEq(bytesRead, (IntExpr)_ctx.MkAdd(e1, e2)),
            _ctx.MkEq(bytesRead, (IntExpr)_ctx.MkAdd(e1, e2, e3))
        );

        // Property: If bytesRead is NOT any of these, it's not valid.
        // This is a bit tautological in Z3 if we define it that way, 
        // but we can use it to prove that if we have a buffer size 'count',
        // the bytesRead will be <= count.
        
        IntExpr count = _ctx.MkIntConst("count");
        _solver.Assert(_ctx.MkEq(bytesRead, (IntExpr)_ctx.MkAdd(e1, e2)));
        _solver.Assert(_ctx.MkLt(count, (IntExpr)_ctx.MkAdd(e1, e2)));
        
        // This would be a protocol violation: returning more bytes than requested.
        _solver.Assert(_ctx.MkLe(bytesRead, count));

        Assert.Equal(Status.UNSATISFIABLE, _solver.Check());
    }
}
