# 9P2000 Protocol Compliance Matrix (Kernel Core Focus)

This matrix tracks protocol-obedience goals for the F# kernel core and links each rule to executable checks.

## Scope

- Dialect: `9P2000` only (no `.u`, no `.L` in kernel proof targets).
- Proof style:
  - `SMT (Z3)` for symbolic invariants over state transitions.
  - `Property/Fuzz` for parser and concrete implementation behavior.
  - `Coyote` for concurrency/scheduling invariants.

## Matrix

| Area | Documented Behavior | Check Type | Status | Evidence |
|---|---|---|---|---|
| Walk root bound | `..` must not escape root | Z3 proof (bounded symbolic traces) | Implemented | `Z3ProtocolWalkProofTests.Z3_Walk_Depth_Never_Goes_Negative_For_Bounded_Traces` |
| Walk determinism / no sticky state | Same initial state + same path => same result | Z3 proof | Implemented | `Z3ProtocolWalkProofTests.Z3_NoStickyDelegation_Result_Is_Function_Of_Inputs` |
| Walk implementation alignment | F# `ChannelOps.walk` matches symbolic transition model | Property + Z3 counterexample check | Implemented | `Z3ProtocolWalkProofTests.Z3_Model_Matches_ChannelOps_Walk_For_Fuzzed_Segments` |
| Namespace longest-prefix resolution | Most specific mount must win | Property tests | Implemented | `NamespaceTests.Resolve_Uses_Longest_Prefix_Match` |
| Bind replace semantics (`MREPL`) | Target mount replaced deterministically | Property + Z3 | Implemented | `BindRobustnessTests.Bind_MREPL_Is_Idempotent`, `Z3NamespaceBindProofTests.Z3_Model_Matches_NamespaceOps_For_MRepl` |
| Bind union ordering (`MBEFORE`/`MAFTER`) | Search order must follow flag semantics | Property + Z3 | Implemented | `NamespaceTests.Bind_Before_Union_Prioritizes_New_Backend`, `NamespaceTests.Bind_After_Union_Appends_New_Backend`, `Z3NamespaceBindProofTests.Z3_Model_Matches_NamespaceOps_For_MBefore`, `Z3NamespaceBindProofTests.Z3_Model_Matches_NamespaceOps_For_MAfter` |
| Bind algebraic contradictions | Illegal ordering/idempotence contradictions must be UNSAT | Z3 proof | Implemented | `Z3NamespaceBindProofTests.Z3_Bind_MBefore_Ordering_Contradiction_Is_Unsat_Bounded`, `Z3NamespaceBindProofTests.Z3_Bind_MAfter_Ordering_Contradiction_Is_Unsat_Bounded`, `Z3NamespaceBindProofTests.Z3_Bind_MRepl_Idempotence_Contradiction_Is_Unsat_Bounded` |
| Process namespace isolation | Child bind changes must not leak to parent | Coyote + property tests | Implemented | `BindRobustnessTests.Coyote_Process_Namespace_Isolation_Test`, `Plan9ProcessTests.Fork_Inherits_FdTable_And_Child_Bind_Does_Not_Leak` |
| Parser bounded failure | Malformed frames must produce bounded error, not crash | Property/Fuzz | Implemented | `ParserPropertyTests`, `ParserRobustnessTests` |
| Readdir pagination continuity | Non-zero offsets must produce continuation, not entry loss | Property tests + Z3 proof | Implemented | `RootFileSystemMutationPropertyTests.Readdir_Pagination_NonZeroOffset_Returns_FollowOnEntries_Property`, `Z3ReaddirMonotonicityProofTests.Z3_Readdir_Offset_Monotonicity_Invariant` |
| FID lifecycle protocol | Unknown/expired FIDs return protocol errors | Property + Coyote + Z3 proof | Implemented | `DispatcherProtocolPropertyFuzzTests`, `DispatcherUnknownFidCoyoteTests`, `Z3FidLifecycleProofTests` |
| Session state negotiation | Tversion must be first and governs msize | Z3 proof | Implemented | `Z3ProtocolStateProofTests.Z3_Tversion_Msize_Negotiation`, `Z3ProtocolStateProofTests.Z3_Tattach_Requires_Negotiated_State` |
| Vault Security | Arena Isolation (No Overlap) | Z3 proof | Implemented | `Z3VaultSecurityProofTests.Z3_Arena_NoOverlap_Invariant` |
| Vault Security | Zero-Exposure (Zero-on-Free) | Z3 proof | Implemented | `Z3VaultSecurityProofTests.Z3_Vault_SecretZeroing_PostCondition` |
| Vault Security | Session Key Privacy (Mixing) | Z3 proof | Implemented | `Z3VaultSecurityProofTests.Z3_Vault_SessionKey_Mix_Isolation` |
| Vault Security | Elligator Consistency | Z3 proof | Implemented | `Z3VaultSecurityProofTests.Z3_Elligator_Rev_Consistency_Invariant` |
| Vault Security | Elligator Deniability | Z3 proof | Implemented | `Z3VaultSecurityProofTests.Z3_Elligator_Deniability_Invariant` |

## Next Proof Targets

1. Add SMT proof for `clunk` and `remove` idempotence across bounded transaction traces.
2. Add SMT proof for `Tflush` vs. outstanding tag retirement synchronization.
