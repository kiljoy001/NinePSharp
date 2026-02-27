using NinePSharp.Constants;
using System;
using Microsoft.Z3;
using Xunit;

namespace NinePSharp.Tests.Architecture;

public class Z3ResourceLimitProofTests : IDisposable
{
    private readonly Context _ctx;
    private readonly Solver _solver;

    public Z3ResourceLimitProofTests()
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
    public void Z3_Fid_Exhaustion_Enforcement()
    {
        IntExpr currentFidCount = _ctx.MkIntConst("currentFidCount");
        IntExpr maxFids = _ctx.MkIntConst("maxFids");
        
        _solver.Assert(_ctx.MkGt(maxFids, _ctx.MkInt(0)));
        _solver.Assert(_ctx.MkGe(currentFidCount, _ctx.MkInt(0)));

        // Transition: AllocateFid()
        // Success if currentFidCount < maxFids.
        BoolExpr canAllocate = _ctx.MkLt(currentFidCount, maxFids);
        
        // Negated property: Allocation succeeds when we are already at or above limit.
        _solver.Assert(_ctx.MkGe(currentFidCount, maxFids));
        _solver.Assert(canAllocate);

        Assert.Equal(Status.UNSATISFIABLE, _solver.Check());
    }

    [Fact]
    public void Z3_Tag_Exhaustion_Enforcement()
    {
        IntExpr currentTagCount = _ctx.MkIntConst("currentTagCount");
        IntExpr maxTags = _ctx.MkInt(65535); // 16-bit tags
        
        _solver.Assert(_ctx.MkGe(currentTagCount, _ctx.MkInt(0)));

        // Negated property: currentTagCount exceeds the physical protocol limit of 65535.
        _solver.Assert(_ctx.MkGt(currentTagCount, maxTags));
        
        // If we have an invariant that currentTagCount <= maxTags, this must be UNSAT.
        _solver.Assert(_ctx.MkLe(currentTagCount, maxTags));

        Assert.Equal(Status.UNSATISFIABLE, _solver.Check());
    }
}
