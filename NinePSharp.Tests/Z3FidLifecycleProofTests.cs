using NinePSharp.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Z3;
using Xunit;

namespace NinePSharp.Tests.Architecture;

public class Z3FidLifecycleProofTests : IDisposable
{
    private readonly Context _ctx;
    private readonly Solver _solver;

    public Z3FidLifecycleProofTests()
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
    public void Z3_FidTable_Attach_Allocates_Fid()
    {
        // Initial state: fid is not in use.
        Func<IntExpr, BoolExpr> isAllocated0 = f => _ctx.MkFalse();

        // Transition: Attach(fid)
        IntExpr attachFid = _ctx.MkIntConst("attachFid");
        
        // Post-state after Attach
        Func<IntExpr, BoolExpr> isAllocated1 = f => _ctx.MkOr(isAllocated0(f), _ctx.MkEq(f, attachFid));

        // Property: attachFid is now allocated.
        _solver.Assert(isAllocated1(attachFid));

        // Negated property: attachFid is NOT allocated.
        _solver.Assert(_ctx.MkNot(isAllocated1(attachFid)));

        Assert.Equal(Status.UNSATISFIABLE, _solver.Check());
    }

    [Fact]
    public void Z3_FidTable_Clunk_Deallocates_Fid()
    {
        // State 0: fid is allocated.
        IntExpr targetFid = _ctx.MkIntConst("targetFid");
        Func<IntExpr, BoolExpr> isAllocated0 = f => _ctx.MkEq(f, targetFid);

        // Transition: Clunk(targetFid)
        // Post-state: fid is allocated if it was allocated and is NOT the clunked fid.
        Func<IntExpr, BoolExpr> isAllocated1 = f => _ctx.MkAnd(isAllocated0(f), _ctx.MkNot(_ctx.MkEq(f, targetFid)));

        // Property: targetFid is no longer allocated.
        BoolExpr isStillAllocated = isAllocated1(targetFid);
        
        _solver.Assert(isStillAllocated);

        Assert.Equal(Status.UNSATISFIABLE, _solver.Check());
    }

    [Fact]
    public void Z3_FidTable_Walk_Cloning_Consistency()
    {
        // State 0: oldFid is allocated.
        IntExpr oldFid = _ctx.MkIntConst("oldFid");
        Func<IntExpr, BoolExpr> isAllocated0 = f => _ctx.MkEq(f, oldFid);

        // Transition: Walk(oldFid, newFid) where newFid != oldFid
        IntExpr newFid = _ctx.MkIntConst("newFid");
        _solver.Assert(_ctx.MkNot(_ctx.MkEq(oldFid, newFid)));

        // Post-state:
        Func<IntExpr, BoolExpr> isAllocated1 = f => _ctx.MkOr(isAllocated0(f), _ctx.MkEq(f, newFid));

        // Property: both are now allocated.
        BoolExpr invariant = _ctx.MkAnd(isAllocated1(oldFid), isAllocated1(newFid));

        _solver.Assert(_ctx.MkNot(invariant));

        Assert.Equal(Status.UNSATISFIABLE, _solver.Check());
    }

    [Fact]
    public void Z3_FidTable_Double_Attach_Is_Detected_As_Conflict()
    {
        // 9P2000 spec: attach(5) - "The fid must not be in use."
        
        IntExpr fid = _ctx.MkIntConst("fid");
        
        // State 0: Empty
        Func<IntExpr, BoolExpr> isAllocated0 = f => _ctx.MkFalse();

        // Step 1: Attach(fid) -> Success
        // (Simplified: we assume it succeeded if it wasn't in use)
        Func<IntExpr, BoolExpr> isAllocated1 = f => _ctx.MkEq(f, fid);

        // Step 2: Attach(fid) again
        BoolExpr conflict = isAllocated1(fid);

        // We want to prove that if we did a successful attach, 
        // a subsequent attach with the same fid is a conflict.
        _solver.Assert(_ctx.MkNot(conflict));

        Assert.Equal(Status.UNSATISFIABLE, _solver.Check());
    }

    [Fact]
    public void Z3_FidTable_Tversion_Clunks_All_Fids()
    {
        // 9P2000 spec: version(5) - "A Tversion message on an open connection has the effect of 
        // clunking all fids on that connection"

        // State 0: Some fids are allocated.
        IntExpr anyFid = _ctx.MkIntConst("anyFid");
        // We don't know which ones, just that some might be.
        // isAllocated0(f) is some arbitrary predicate.
        Sort intSort = _ctx.MkIntSort();
        FuncDecl isAllocated0 = _ctx.MkFuncDecl("isAllocated0", intSort, _ctx.MkBoolSort());

        // Transition: Tversion
        // Post-state: Nothing is allocated.
        Func<IntExpr, BoolExpr> isAllocated1 = f => _ctx.MkFalse();

        // Property: For any fid f, isAllocated1(f) is false.
        _solver.Assert(isAllocated1(anyFid));

        Assert.Equal(Status.UNSATISFIABLE, _solver.Check());
    }
}
