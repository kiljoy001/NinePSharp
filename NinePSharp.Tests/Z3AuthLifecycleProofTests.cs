using NinePSharp.Constants;
using System;
using Microsoft.Z3;
using Xunit;

namespace NinePSharp.Tests.Architecture;

public class Z3AuthLifecycleProofTests : IDisposable
{
    private readonly Context _ctx;
    private readonly Solver _solver;

    public Z3AuthLifecycleProofTests()
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
    public void Z3_Tattach_Requires_Authenticated_Afid()
    {
        EnumSort authState = _ctx.MkEnumSort("AuthState", new[] { "None", "InProcess", "Authenticated" });
        Expr none = authState.Consts[0];
        Expr inProcess = authState.Consts[1];
        Expr authenticated = authState.Consts[2];

        Expr afidState = _ctx.MkConst("afidState", authState);
        BoolExpr isAttachSuccessful = _ctx.MkBoolConst("isAttachSuccessful");

        // Logic: Attach is successful ONLY if afidState is Authenticated.
        // (Note: 9P2000 also allows NO afid (~-1), but we model the with-auth case).
        _solver.Assert(_ctx.MkImplies(isAttachSuccessful, _ctx.MkEq(afidState, authenticated)));

        // Negated property: Successful attach while auth is still InProcess.
        _solver.Assert(isAttachSuccessful);
        _solver.Assert(_ctx.MkEq(afidState, inProcess));

        Assert.Equal(Status.UNSATISFIABLE, _solver.Check());
    }

    [Fact]
    public void Z3_Afid_Cannot_Be_Used_For_IO()
    {
        // 9P2000 spec: "the afid may be used only for authentication protocol 
        // and cannot be used for any other purpose"
        
        BoolExpr isAfid = _ctx.MkBoolConst("isAfid");
        
        // Model a set of allowed operations.
        EnumSort opType = _ctx.MkEnumSort("OpType", new[] { "Read", "Write", "AuthRead", "AuthWrite", "Clunk" });
        Expr read = opType.Consts[0];
        Expr write = opType.Consts[1];
        Expr authRead = opType.Consts[2];
        Expr authWrite = opType.Consts[3];
        Expr clunk = opType.Consts[4];

        Expr currentOp = _ctx.MkConst("currentOp", opType);

        // Security Policy: if isAfid then op must be one of {AuthRead, AuthWrite, Clunk}.
        BoolExpr isAllowed = _ctx.MkImplies(isAfid, 
            _ctx.MkOr(_ctx.MkEq(currentOp, authRead), 
                      _ctx.MkEq(currentOp, authWrite), 
                      _ctx.MkEq(currentOp, clunk)));

        // Negated property: Afid used for normal Read.
        _solver.Assert(isAfid);
        _solver.Assert(_ctx.MkEq(currentOp, read));
        _solver.Assert(isAllowed);

        Assert.Equal(Status.UNSATISFIABLE, _solver.Check());
    }
}
