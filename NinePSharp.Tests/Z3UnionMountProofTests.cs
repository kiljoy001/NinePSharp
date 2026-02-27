using NinePSharp.Constants;
using System;
using System.Linq;
using Microsoft.Z3;
using Xunit;

namespace NinePSharp.Tests.Architecture;

public class Z3UnionMountProofTests : IDisposable
{
    private readonly Context _ctx;
    private readonly Solver _solver;

    public Z3UnionMountProofTests()
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
    public void Z3_UnionMount_HigherPriority_Shadows_Lower()
    {
        // Model two backends in a union.
        // Priority 0 is highest.
        
        IntExpr backend1_priority = _ctx.MkInt(0);
        IntExpr backend2_priority = _ctx.MkInt(1);

        BoolExpr fileExistsInB1 = _ctx.MkBoolConst("fileExistsInB1");
        BoolExpr fileExistsInB2 = _ctx.MkBoolConst("fileExistsInB2");

        // Resolve logic: pick backend with lowest priority value (highest priority) that has the file.
        // selected_priority = if existsInB1 then B1_priority 
        //                     else if existsInB2 then B2_priority 
        //                     else -1 (not found)
        
        IntExpr selectedPriority = (IntExpr)_ctx.MkITE(
            fileExistsInB1, 
            backend1_priority,
            _ctx.MkITE(fileExistsInB2, backend2_priority, _ctx.MkInt(-1))
        );

        // Scenario: File exists in BOTH.
        _solver.Assert(fileExistsInB1);
        _solver.Assert(fileExistsInB2);

        // Property: Backend 1 must be selected.
        // Negated: Backend 2 was selected instead.
        _solver.Assert(_ctx.MkEq(selectedPriority, backend2_priority));

        Assert.Equal(Status.UNSATISFIABLE, _solver.Check());
    }

    [Fact]
    public void Z3_Readdir_Union_Completeness()
    {
        // Prove that every entry from every backend is represented in the union readdir
        // unless it is shadowed.
        
        Sort entrySort = _ctx.MkIntSort();
        FuncDecl existsInB1 = _ctx.MkFuncDecl("existsInB1", entrySort, _ctx.MkBoolSort());
        FuncDecl existsInB2 = _ctx.MkFuncDecl("existsInB2", entrySort, _ctx.MkBoolSort());
        FuncDecl existsInUnion = _ctx.MkFuncDecl("existsInUnion", entrySort, _ctx.MkBoolSort());

        IntExpr e = _ctx.MkIntConst("e");

        // Union logic: entry is in union if it's in B1 OR it's in B2.
        _solver.Assert(_ctx.MkForall(new[] { e }, 
            _ctx.MkIff((BoolExpr)existsInUnion.Apply(e), 
                       _ctx.MkOr((BoolExpr)existsInB1.Apply(e), (BoolExpr)existsInB2.Apply(e)))));

        // Negated property: Entry exists in B2 but is missing from Union.
        _solver.Assert((BoolExpr)existsInB2.Apply(e));
        _solver.Assert(_ctx.MkNot((BoolExpr)existsInUnion.Apply(e)));

        Assert.Equal(Status.UNSATISFIABLE, _solver.Check());
    }
}
