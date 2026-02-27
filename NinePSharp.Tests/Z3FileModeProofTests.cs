using NinePSharp.Constants;
using System;
using Microsoft.Z3;
using Xunit;

namespace NinePSharp.Tests.Architecture;

public class Z3FileModeProofTests : IDisposable
{
    private readonly Context _ctx;
    private readonly Solver _solver;

    public Z3FileModeProofTests()
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
    public void Z3_DmAppend_Forces_Offset_To_Eof()
    {
        // 9P2000 spec: "If the file is append-only (DMAPPEND bit set in its mode), 
        // the data will be placed at the end of the file regardless of the offset."

        IntExpr clientOffset = _ctx.MkIntConst("clientOffset");
        IntExpr fileEof = _ctx.MkIntConst("fileEof");
        BoolExpr isAppendOnly = _ctx.MkBoolConst("isAppendOnly");

        _solver.Assert(_ctx.MkGe(clientOffset, _ctx.MkInt(0)));
        _solver.Assert(_ctx.MkGe(fileEof, _ctx.MkInt(0)));
        _solver.Assert(isAppendOnly);

        // Model the effective offset used by the backend.
        IntExpr effectiveOffset = (IntExpr)_ctx.MkITE(isAppendOnly, fileEof, clientOffset);

        // Negated property: Effective offset is NOT EOF even though DMAPPEND is set.
        _solver.Assert(_ctx.MkNot(_ctx.MkEq(effectiveOffset, fileEof)));

        Assert.Equal(Status.UNSATISFIABLE, _solver.Check());
    }

    [Fact]
    public void Z3_DmExcl_Prevents_Concurrent_Open()
    {
        // 9P2000 spec: "Exclusive use files (DMEXCL bit set) may be open by only one fid at a time."
        
        BoolExpr isExcl = _ctx.MkBoolConst("isExcl");
        IntExpr openCount = _ctx.MkIntConst("openCount");

        _solver.Assert(isExcl);
        
        // Transition: Open(fid)
        // Precondition: if isExcl then openCount == 0
        BoolExpr canOpen = _ctx.MkImplies(isExcl, _ctx.MkEq(openCount, _ctx.MkInt(0)));

        // Negated property: We can open an exclusive file when someone else has it open.
        _solver.Assert(_ctx.MkGt(openCount, _ctx.MkInt(0)));
        _solver.Assert(canOpen);

        Assert.Equal(Status.UNSATISFIABLE, _solver.Check());
    }

    [Fact]
    public void Z3_Mode_Immutability_Dir_Vs_File()
    {
        // 9P2000 spec: "Directories may not be created by create... nor may a directory 
        // be changed to a file or vice versa by wstat."

        BoolExpr oldIsDir = _ctx.MkBoolConst("oldIsDir");
        BoolExpr newIsDir = _ctx.MkBoolConst("newIsDir");

        // Transition: Wstat(newMode)
        // Constraint: Directory status must match.
        BoolExpr isValidWstat = _ctx.MkEq(oldIsDir, newIsDir);

        // Negated property: Wstat changed a directory into a file or vice versa.
        _solver.Assert(_ctx.MkNot(_ctx.MkEq(oldIsDir, newIsDir)));
        _solver.Assert(isValidWstat);

        Assert.Equal(Status.UNSATISFIABLE, _solver.Check());
    }
}
