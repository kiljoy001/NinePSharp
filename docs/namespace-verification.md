# Namespace verification

## Resource authorization layer — 2026-10-01

`NinePSharp.Namespaces.Authorization` is a separate layer over the provider interface used by
`LocalNamespaceDataPlane`; `NinePSharp.Namespaces` is unchanged. A view for one principal
intersects an enabled principal under the current policy generation, user and group grants
(`self` or provider-attested `tree`), the 9front gefs `fsaccess` mode check and read-only mount
roots. Rules follow 9front `sys/src/cmd/gefs/fs.c` (`fsaccess`, `ingroup`, `mode2bits`,
`fswalk`, `fscreate`, `fsremove`) and the remove-on-close parent check of hjfs `fs2.c`. A user is
a member of its same-name group; `none` gets only the other set; `nogroup` members lose the other
set except to search directories. Objects with no granted right look absent. wstat is not exposed.

- Features: Fog's `docs/specifications/fog-foundation/ResourceAuthorization.feature` (design) and the
  executable `NinePSharp.Namespaces.Authorization.Tests/Features/ResourceAuthorization.feature`.
- Tests: 96 passed, including the scenarios, construction unit tests and the shared fuzz model
  (`NinePSharp.Fuzzer/AuthorizationFuzz.cs`) run as FsCheck properties.
- Coverage: 100% lines, branches and methods. The standard gate first showed one uncovered
  branch (remove through an open handle); a scenario now covers it.
- Stryker (`stryker-config-namespaces-authorization.json`): 100%, 275 killed, 0 timeouts,
  0 survivors, 0 uncovered. No exclusions. Equivalent mutants were removed by restructuring
  (left-shifted permission masks, single-expression open requirements, nullable ancestry chain).
  This also fixed an off-by-one: exactly `MaxAncestryDepth` ancestors was treated as unproven.
- AFL `scripts/fuzz.sh authorization`, 60 s: 650,388 executions, 0 crashes, 0 hangs.
- `bash scripts/run-quality.sh`: passed; merged coverage 85.63% lines, 76.59% branches.

Not yet done: Fog per-principal views (`NamespaceViews.feature`), the LibTab policy bundle and
epoch administration, job scopes, wstat authorization and union create selection under policy.

## Strict timeout policy — 2026-09-16

`tools/mutation_summary.py` now reports timed-out mutants separately and counts
them against the mutation score. The 100% pipeline gate requires actual test
kills for every covered, compilable, non-ignored mutant: timeouts, survivors and
uncovered mutants fail it. Earlier results below used Stryker's timeout-inclusive
score and are historical evidence, not passes under this stricter policy.

## Local descriptor ownership — 2026-09-14

`DescriptorGroup` now provides the local Fgrp ownership layer: independent
share/copy/empty inheritance, lowest-free allocation, duplicate replacement and
capacity limits, per-slot close-on-exec, and final channel-reference release.
`DescriptorLease` pins a channel for admitted I/O. Process termination releases
its membership; a shared child retains its table. Final table cleanup detaches
slots before invoking provider callbacks and releases them in ascending fd order.
Provider close errors follow native `cclose` behavior and do not restore slots.

`DescriptorGroupTests` includes a generated ownership property and regressions
for repeated lease disposal, sparse copied-table capacity, callbacks consulting
the process table, blocked cleanup, and provider close failure. Executable
`Features/DescriptorLifetimes.feature` adds 11 BDD cases, and
`Features/DescriptorCleanup.feature` adds six explicit cleanup cases. These tests cover local
ownership, not durable Orleans recovery, shared file offsets, or fd syscall routing.

Final standard validation: `bash scripts/run-quality.sh` passed 793 tests,
including 219 namespace tests, with zero failed/skipped. Coverage and CRAP gates
passed. Log: `.artifacts/quality-namespace-paths.log`. NuGet emitted NU1900 for
unavailable package vulnerability metadata; this run does not establish a fresh
dependency vulnerability audit.

Semgrep is now installed. The two existing custom rules include namespace and
Orleans namespace sources. Their repository-root paths were made explicit to
remove path-interpretation warnings; the final standalone scan passed with zero
findings across 45 targets (`.artifacts/semgrep-descriptors.log`).

The final `bash scripts/fuzz.sh namespace-syscalls` campaign ran for 10 seconds:
40,111 executions, zero saved crashes/hangs. Its new ownership oracle retains an
I/O lease across process exit and requires exactly one provider close after the
last release. Log: `.artifacts/fuzz-descriptor-lifetimes.log`.

The first expanded namespace mutation run found four survivors in lease disposal,
copy capacity, and provider callback scheduling. Focused regressions were added;
the final full namespace rerun, including the cleanup BDD feature, passed at
100%: 602 killed, 13 detected by timeout, zero survivors and zero
uncovered mutants. Existing filters ignored 359 mutants and 111 caused
compile errors; these are not test kills. No new mutation exclusions were added,
and `DescriptorGroup.cs` is included in the 100% gate. Report:
`.artifacts/stryker-descriptors/reports/mutation-report.json`.

Run from `NinePSharp.Namespaces.Tests`:

```sh
dotnet stryker --config-file ../stryker-config-namespaces.json \
  --reporter json --reporter progress --output ../.artifacts/stryker-descriptors \
  --skip-version-check --break-on-initial-test-failure --verbosity info
```

## Lifecycle design and unmount regressions — 2026-09-13

[ResourceLifetimes.md](specifications/plan9-namespace/ResourceLifetimes.md) records
the native ownership rules checked against `../9front` manuals and source, and
separately defines the durable Orleans ownership/recovery contract. Its new
descriptor and distributed lifecycle feature files are acceptance specifications
with implementation and executable bindings still pending.

The executable `FidUnmount.feature` covers 12 combinations of open/unopened
regular-file fids, selected/complete unmount or replacement, and clunk/disconnect
cleanup. Every case checks retained resource identity and I/O, a fresh walk's
current namespace selection, and provider close count. Providers remain available
in these local scenarios; union-directory cursor parity and provider outage
recovery are not established by them.

Validation:

- `dotnet test NinePSharp.Namespaces.Tests/NinePSharp.Namespaces.Tests.csproj --no-restore --filter FullyQualifiedName~NamespaceFidsSurviveUnmount -v minimal`:
  12 passed, zero failed/skipped (`.artifacts/fid-unmount-tests.log`).
- `bash scripts/run-quality.sh`: 750 tests passed, including 176 namespace tests;
  zero failed/skipped, coverage and CRAP gates passed. Optional Semgrep rules were
  skipped because Semgrep is not installed. Log: `.artifacts/quality-namespace-paths.log`.
- The two new acceptance feature files and executable unmount feature parsed
  with Gherkin 29.0.0; `git diff --check` passed.

This update does not refresh mutation or fuzz evidence. The runs below are dated
historical results, not validation of every subsequent working-tree change.

## Earlier verification — 2026-09-12

The symbolic-math runtime, job adapter, client, demo, dependencies, and associated
tests and specifications have been removed. The current focus is the Plan 9
namespace implementation and its Orleans adapters. Generic Fog transactions and
9P transport remain available; workload execution is deferred.

## Namespace mutation evidence

The namespace scope includes mount semantics, navigation, virtual processes,
syscalls, data operations, fid sessions, and the process control filesystem.
`NamespaceSession.cs` and `NamespaceControlResource.cs` are included.

The isolated Stryker run in `.artifacts/stryker-namespaces-final` passed at 100%:
485 killed mutants, 9 detected by timeout, zero survivors, and zero uncovered
mutants. Stryker separately reported 87 compile-error mutants and 303 ignored
mutants under its existing filters; these are not test kills. No new mutation
suppressions were introduced. Local and CI thresholds are now 100%.

The Orleans adapter scope in `.artifacts/stryker-orleans-final` also passed at
100%: 54 killed, 1 detected by timeout, zero survivors, and zero uncovered mutants.
Its existing filters ignored 161 mutants, and 3 produced compile errors. The
added contracts exercise distributed creation and validation before grain lookup.

The CI protocol scope in `.artifacts/stryker-core-final` passed with 130 killed,
zero survivors, and zero uncovered mutants; 33 were ignored by existing filters
and 4 produced compile errors.

Run from `NinePSharp.Namespaces.Tests`:

```sh
dotnet stryker --config-file ../stryker-config-namespaces.json \
  --reporter json --output ../.artifacts/stryker-namespaces-final \
  --skip-version-check --verbosity info
```

Run mutation tests separately from other builds and test commands. A concurrent
build can replace instrumented dependencies and invalidate the result.

The added regressions cover cancellation and lock ordering, disposal during
provider calls, fid publication and cleanup, operation-ID exhaustion, control-file
commands and metadata, large read offsets/counts, and mount-policy validation.
The read tests exposed an overflow in control-file reads: counts must be clamped
to the available data before conversion to CLR array indices.

## Standard validation

`bash scripts/run-quality.sh` passed after the removal and namespace fixes:
814 tests passed, none failed or skipped, coverage thresholds passed, and no
methods exceeded the CRAP limit of 30. Semgrep was not installed, so the script
skipped its optional custom rules. The log is
`.artifacts/quality-namespace-final.log`.

All 36 specification feature files parsed with Gherkin 29.0.0. This checks syntax,
not execution of the proposed scenarios. The retained `status-succeeded.tab` and
`runtime-lock.tab` examples parsed and reserialized byte-for-byte through LibTab.

Both 10-second SharpFuzz/AFL++ smoke campaigns passed with zero saved crashes or
hangs: `bash scripts/fuzz.sh namespace` executed 52,198 inputs and
`bash scripts/fuzz.sh namespace-syscalls` executed 68,798 inputs. These bounded
campaigns provide regression evidence, not exhaustive correctness proofs.

This result applies to the namespace scope. It does not establish a passing
repository-wide mutation gate or complete Plan 9 compatibility; see the
[implementation limits](plan9-namespace-semantics.md#current-limits).
