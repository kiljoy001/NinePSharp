using NinePSharp.Constants;
using System;
using Microsoft.Z3;
using Xunit;

namespace NinePSharp.Tests.Architecture;

public class Z3IdempotenceProofTests : IDisposable
{
    private readonly Context _ctx;
    private readonly Solver _solver;

    public Z3IdempotenceProofTests()
    {
        _ctx = new Context();
        _solver = _ctx.MkSolver();
    }

    public void Dispose()
    {
        _solver.Dispose();
        _ctx.Dispose();
    }

    /// <summary>
    /// Proves that clunk(f) followed by clunk(f) results in the same fid allocation state
    /// as a single clunk(f).
    /// </summary>
    [Fact]
    public void Z3_Clunk_Is_State_Idempotent()
    {
        Sort intSort = _ctx.MkIntSort();
        FuncDecl isAllocated = _ctx.MkFuncDecl("isAllocated", intSort, _ctx.MkBoolSort());
        IntExpr fid = _ctx.MkIntConst("fid");
        
        FuncDecl isAllocated1 = _ctx.MkFuncDecl("isAllocated1", intSort, _ctx.MkBoolSort());
        FuncDecl isAllocated2 = _ctx.MkFuncDecl("isAllocated2", intSort, _ctx.MkBoolSort());

        IntExpr x = _ctx.MkIntConst("x");

        // Axiom for S1: isAllocated1(x) == (isAllocated(x) && x != fid)
        _solver.Assert(_ctx.MkForall(new Expr[] { x }, 
            _ctx.MkEq(_ctx.MkApp(isAllocated1, x), 
                     _ctx.MkAnd((BoolExpr)_ctx.MkApp(isAllocated, x), _ctx.MkNot(_ctx.MkEq(x, fid))))));

        // Axiom for S2: isAllocated2(x) == (isAllocated1(x) && x != fid)
        _solver.Assert(_ctx.MkForall(new Expr[] { x }, 
            _ctx.MkEq(_ctx.MkApp(isAllocated2, x), 
                     _ctx.MkAnd((BoolExpr)_ctx.MkApp(isAllocated1, x), _ctx.MkNot(_ctx.MkEq(x, fid))))));

        // Theorem: forall x, isAllocated1(x) == isAllocated2(x)
        BoolExpr theorem = _ctx.MkForall(new Expr[] { x }, _ctx.MkEq(_ctx.MkApp(isAllocated1, x), _ctx.MkApp(isAllocated2, x)));

        // Negate theorem to find counter-example
        _solver.Assert(_ctx.MkNot(theorem));

        // If UNSAT, then the theorem is proved.
        Assert.Equal(Status.UNSATISFIABLE, _solver.Check());
    }

    /// <summary>
    /// Proves that remove(f) followed by remove(f) results in the same state
    /// (fid deallocated, file gone) as a single remove(f).
    /// </summary>
    [Fact]
    public void Z3_Remove_Is_State_Idempotent()
    {
        Sort intSort = _ctx.MkIntSort();
        
        // State: fids and files
        FuncDecl isAllocated = _ctx.MkFuncDecl("isAllocated", intSort, _ctx.MkBoolSort());
        FuncDecl fileExists = _ctx.MkFuncDecl("fileExists", intSort, _ctx.MkBoolSort());
        FuncDecl fidToFile = _ctx.MkFuncDecl("fidToFile", intSort, intSort);

        IntExpr fid = _ctx.MkIntConst("fid");
        IntExpr fileId = (IntExpr)_ctx.MkApp(fidToFile, fid);

        // Transition: Remove(fid)
        FuncDecl isAllocated1 = _ctx.MkFuncDecl("isAllocated1", intSort, _ctx.MkBoolSort());
        FuncDecl fileExists1 = _ctx.MkFuncDecl("fileExists1", intSort, _ctx.MkBoolSort());

        IntExpr x = _ctx.MkIntConst("x");

        // S1 Axioms
        _solver.Assert(_ctx.MkForall(new Expr[] { x }, 
            _ctx.MkEq(_ctx.MkApp(isAllocated1, x), 
                     _ctx.MkAnd((BoolExpr)_ctx.MkApp(isAllocated, x), _ctx.MkNot(_ctx.MkEq(x, fid))))));
        
        _solver.Assert(_ctx.MkForall(new Expr[] { x }, 
            _ctx.MkEq(_ctx.MkApp(fileExists1, x), 
                     _ctx.MkAnd((BoolExpr)_ctx.MkApp(fileExists, x), _ctx.MkNot(_ctx.MkEq(x, fileId))))));

        // S2 Transition (applying remove again)
        FuncDecl isAllocated2 = _ctx.MkFuncDecl("isAllocated2", intSort, _ctx.MkBoolSort());
        FuncDecl fileExists2 = _ctx.MkFuncDecl("fileExists2", intSort, _ctx.MkBoolSort());

        _solver.Assert(_ctx.MkForall(new Expr[] { x }, 
            _ctx.MkEq(_ctx.MkApp(isAllocated2, x), 
                     _ctx.MkAnd((BoolExpr)_ctx.MkApp(isAllocated1, x), _ctx.MkNot(_ctx.MkEq(x, fid))))));

        _solver.Assert(_ctx.MkForall(new Expr[] { x }, 
            _ctx.MkEq(_ctx.MkApp(fileExists2, x), 
                     _ctx.MkAnd((BoolExpr)_ctx.MkApp(fileExists1, x), _ctx.MkNot(_ctx.MkEq(x, fileId))))));

        // Theorem: S1 == S2
        BoolExpr theorem = _ctx.MkAnd(
            _ctx.MkForall(new Expr[] { x }, _ctx.MkEq(_ctx.MkApp(isAllocated1, x), _ctx.MkApp(isAllocated2, x))),
            _ctx.MkForall(new Expr[] { x }, _ctx.MkEq(_ctx.MkApp(fileExists1, x), _ctx.MkApp(fileExists2, x)))
        );

        _solver.Assert(_ctx.MkNot(theorem));

        Assert.Equal(Status.UNSATISFIABLE, _solver.Check());
    }
}
