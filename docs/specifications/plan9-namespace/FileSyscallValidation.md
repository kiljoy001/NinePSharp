# File syscall validation

Since 2026-09-16, the mutation summary gate counts timeouts as failures rather
than kills. Earlier slices below preserve their original, timeout-inclusive
results; current native directory validation uses the stricter gate.

## Initial regular-file slice (2026-09-14)

Validated on 2026-09-14 against the local `../9front` checkout at
`9654fe7fa882f8043c267bbeb6679ebd9102209b`. The consulted source and manual files
had no local modifications:

- `sys/src/9/port/sysfile.c`: `newfd`, `fdtochan`, `openmode`, `read`, `write`, `sseek`.
- `sys/src/9/port/chan.c`: `Aopen` strips `OCEXEC` before provider open.
- `sys/man/2/open`, `sys/man/2/read`, `sys/man/2/seek`.

The implementation supplies path open, regular-file implicit/positioned IO, shared
positions, seek, late-open rejection after exit, and retained IO through close/reuse.
It keeps the native write reservation/correction ordering and the native positioned
IO sentinel. Native create/OEXCL, directory cursors, pipe discrimination, durable
completion recovery, and WASI host integration remain pending. See
[Traceability.md](Traceability.md) for scenario-level status.

### Verification

| Command | Result |
| --- | --- |
| `bash scripts/run-quality.sh` | Passed: 832 tests across eight projects, including 257 namespace and 110 Orleans tests; zero failed or skipped |
| Standard coverage/CRAP gates | Passed: 83.09% line coverage, 72.31% branch coverage, maximum CRAP 30.00 |
| From `NinePSharp.Namespaces.Tests`: `dotnet stryker --config-file ../stryker-config-namespaces.json --reporter json --reporter progress --output ../.artifacts/stryker-syscall-slice --skip-version-check --break-on-initial-test-failure --verbosity error` | Passed: 696 killed, 13 timed out, zero surviving or uncovered mutants; configured score 100% |
| `python3 tools/mutation_summary.py --output-dir .artifacts/stryker-syscall-slice --min-score 100` | Passed; summary counts timeouts together with killed mutants |
| `bash scripts/fuzz.sh namespace-syscalls` | Passed: 10 seconds, 6,093 executions, 152 corpus entries, zero crashes or hangs |
| `semgrep --metrics=off --config quality/semgrep.yml --error --no-git-ignore NinePSharp.Namespaces/Plan9FileSyscalls.cs NinePSharp.Namespaces/DescriptorGroup.cs NinePSharp.Fuzzer/FileSyscallFuzz.cs` | Zero findings; the two production namespace files match the rules' path filters; the fuzzer is outside those filters |
| `git diff --check` | Passed |

The standard build reported NU1900 warnings because NuGet vulnerability metadata
could not be fetched. Compilation and all test gates succeeded. Mutation and fuzz
runs targeted the namespace slice; this is not a claim that the entire `--full`
pipeline was rerun.

Executable BDD covers shared positions, positioned reads, failed-write cleanup,
and an open finishing after its caller exits while a child retains the shared table.
Unit and FsCheck tests cover the remaining implemented boundaries, with barrier-based
tests for overlapping provider waits. The namespace fuzz campaign also checks actual
syscall transfers against an independent position model.

## Native create and OEXCL (2026-09-15)

The next slice implements `CreateAsync` with `Plan9CreateRequest`, following
`chan.c:Acreate` and the `open(2)` manual in the same 9front revision. Ordinary
create opens an existing name with OTRUNC; OEXCL fails without truncating it.
Atomic creation of an absent name remains a create(5) provider responsibility.
The syscall retries lookup only after an acknowledged create rejection; an uncertain
transport failure never triggers truncation. Orleans carries definite rejections
using `ResourceCreateRejectedGrainException` and translates them at the adapter.

Creation consults mounts over retained root/current-directory channels when choosing
the visible name and MCREATE member. Successful creation retains its provider channel
without crossing a new mount, matching `chan.c`. This prevents a final-owner exit
from losing a returned open handle during post-create namespace lookup.

| Command | Result |
| --- | --- |
| `bash scripts/run-quality.sh` | Passed: 866 tests, including 290 namespace tests and 111 Orleans tests; zero failed/skipped. Line coverage 83.19%, branch coverage 72.56%; CRAP and Semgrep passed |
| Namespace Stryker command above, using output `../.artifacts/stryker-create-slice` | Passed: 739 killed, 13 timed out, zero surviving/uncovered mutants; score 100% |
| From `NinePSharp.Namespaces.Orleans.Tests`: `dotnet stryker --config-file ../stryker-config-orleans.json --reporter json --reporter progress --output ../.artifacts/stryker-create-orleans --skip-version-check --break-on-initial-test-failure --verbosity error` | Passed: 57 killed, zero timed out/surviving/uncovered; score 100% |
| `python3 tools/mutation_summary.py --output-dir .artifacts/stryker-create-slice --min-score 100` and the corresponding `stryker-create-orleans` command | Both passed |
| `bash scripts/fuzz.sh namespace-syscalls` | Passed: 10 seconds, 2,795 executions, 180 corpus entries, zero crashes/hangs; includes generated create/exclusive-create model checks |
| `dotnet test NinePSharp.Namespaces.Tests/NinePSharp.Namespaces.Tests.csproj --no-restore -c Release -v minimal` | Final test-warning cleanup verified: 290 passed |
| `semgrep --metrics=off --config quality/semgrep.yml --error --no-git-ignore NinePSharp.Namespaces/Plan9CreateRequest.cs NinePSharp.Namespaces/Plan9FileSyscalls.cs NinePSharp.Namespaces/NamespaceNavigation.cs NinePSharp.Namespaces.Orleans/OrleansResourceOperations.cs` | Four files scanned, zero findings |

Added executable BDD scenarios cover truncate, exclusive collision and full-table
cleanup. Unit/property tests additionally cover competing creates, metadata preservation,
union targets, uncertain failures, and provider completion after process exit.
The real Orleans silo test verifies rejection serialization, preserved contents after
collision, ordinary truncation, and final provider clunks.

Remaining scope: directory cursors and records, pipe discrimination, durable cleanup
and uncertain-outcome reconciliation, production-provider conformance, and WASI hosting.
The standard build still encountered NuGet vulnerability-metadata availability warnings.

## Metadata-backed directory cursors (2026-09-15)

This slice adds bounded, whole 9P2000 stat records to the process syscall API,
shared directory positions through dup and copied descriptor tables, and rewind
that refreshes the metadata listing. The source comparison and explicit adapter
limits are recorded in [DirectoryCursors.md](DirectoryCursors.md).

Executable BDD covers bounded reads, shared cursors, rewind and union ordering.
Unit and property tests cover UTF-8 metadata, record-size boundaries, invalid
offsets, mounted metadata, failed/cancelled reads, serialized rewind, and cleanup
after close, descriptor reuse and process exit. A real Orleans resource-grain test
checks listing and refresh after creation. The syscall fuzz campaign now checks
directory record framing, enumeration order, dup, rewind and exactly-once clunk.

| Command | Result |
| --- | --- |
| `bash scripts/run-quality.sh` | Passed: 888 tests, including 311 namespace and 112 Orleans tests; zero failed/skipped. Line coverage 83.31%, branch coverage 72.84%, maximum CRAP 30.00; Semgrep passed |
| `dotnet test NinePSharp.Namespaces.Tests/NinePSharp.Namespaces.Tests.csproj --no-restore -c Release -v minimal` | Final regression suite passed: 313 tests after adding two direct-host directory write rejection cases |
| From `NinePSharp.Namespaces.Tests`: `dotnet stryker --config-file ../stryker-config-namespaces.json --reporter json --reporter progress --output ../.artifacts/stryker-directory-slice --skip-version-check --break-on-initial-test-failure --verbosity error` | Passed: 795 killed, 15 timed out, zero surviving/uncovered mutants; configured score 100% |
| `python3 tools/mutation_summary.py --output-dir .artifacts/stryker-directory-slice --min-score 100` | Passed; the summary groups the 15 timeouts with killed mutants |
| `bash scripts/fuzz.sh namespace-syscalls` | Passed: 10 seconds, 2,001 executions, 159 corpus entries, zero crashes/hangs |
| `semgrep --metrics=off --config quality/semgrep.yml --error --no-git-ignore NinePSharp.Namespaces/DirectoryCursor.cs NinePSharp.Namespaces/DescriptorGroup.cs NinePSharp.Namespaces/Plan9FileSyscalls.cs` | Three files scanned, zero findings; explicitly includes the new untracked source |

The standard pipeline and targeted namespace mutation/fuzz checks validate this
slice; the repository-wide `--full` pipeline was not rerun. NuGet vulnerability
metadata availability warnings remain. Full native directory-stream parity is
still pending: separate provider/visible offsets, per-member union open/read/clunk,
failed-member skipping, mountfix overflow buffering and live union changes.

## Native provider directory streams (2026-09-16)

Implemented the local streaming contract from [DirectoryStreaming.md](DirectoryStreaming.md)
against the same pinned 9front revision. `DirectoryReadMode.ProviderStream` selects
retained raw provider handles, separate offsets, lazy union enumeration, live
mount-head changes, mounted-stat rewriting, overflow and deferred rewind. The
default metadata adapter remains available. Explicit wire identity mappings are
required for native mounted-entry stat rewriting.

Provider `OpenAsync` abstracts atomic clone/open. Its definite rejection must leave
no acquired internal handle; successful returned handles are owned through final
release. Orleans translates definite directory open/read rejection separately from
transport errors. Unknown member-open errors retain their operation context and
prevent automatic replay. Wstat/fwstat now reconcile through their durable
operation journal; other mutation types retain their separate recovery work.

Five executable BDD cases complement unit/property and async barrier tests. These
cover native zero-count behavior, shared cursors, deferred member close, offset and
record-size boundaries, live union mutations, mountfix tail order, cleanup after
exit and provider failures. Two focused tests inspect retained allocation ownership
and a counter-overflow boundary directly. The real Orleans resource test exercises
raw directory reads, serialized rejection, incremental enumeration and rewind.

Progress-contract checks run before potentially blocking tests and assert cursor,
mount-head and fid-gate release, process termination publication, repeated session
disposal during an admitted operation, and balanced directory cleanup. This makes
missing progress observable as a test failure rather than a runner timeout. The
lowest-free descriptor allocator uses a bounded scan of sorted occupied slots.
The strict summary gate has four regression tests and runs them in the standard
quality pipeline; no additional mutation exclusions were introduced.

| Command | Result |
| --- | --- |
| `bash scripts/run-quality.sh` | Passed: 947 tests across eight projects, including 367 namespace and 115 Orleans tests; zero failed/skipped. Line coverage 83.92%, branch coverage 73.90%, maximum CRAP 30.00; Semgrep passed |
| From `NinePSharp.Namespaces.Tests`: `dotnet stryker --config-file ../stryker-config-namespaces.json --reporter json --reporter progress --output ../.artifacts/stryker-native-directories --skip-version-check --break-on-initial-test-failure --verbosity error` | Passed: 1,029 killed, zero timed out/surviving/uncovered mutants. Supersedes the earlier 1,013 killed / 16 timed-out result |
| `python3 tools/mutation_summary.py --output-dir .artifacts/stryker-native-directories --min-score 100` | Passed at 100% under the strict gate: timeouts count against the score |
| From `NinePSharp.Namespaces.Orleans.Tests`: `dotnet stryker --config-file ../stryker-config-orleans.json --reporter json --reporter progress --output ../.artifacts/stryker-native-directory-orleans --skip-version-check --break-on-initial-test-failure --verbosity error`, then `python3 tools/mutation_summary.py --output-dir .artifacts/stryker-native-directory-orleans --min-score 100` | Passed: 61 killed, zero timed out/surviving/uncovered mutants |
| `bash scripts/fuzz.sh namespace-syscalls` | Passed: 10 seconds, 1,882 executions, 129 corpus entries, zero crashes/hangs; includes native raw-record streams, union enumeration, dup, rewind and open/close balance |
| `semgrep --metrics=off --config quality/semgrep.yml --error --no-git-ignore NinePSharp.Namespaces/DirectoryStreams.cs NinePSharp.Namespaces/DirectoryMounts.cs NinePSharp.Namespaces/DirectoryStatOperations.cs NinePSharp.Namespaces/Plan9FileSyscalls.cs NinePSharp.Namespaces/DescriptorGroup.cs NinePSharp.Namespaces/VirtualProcesses.cs NinePSharp.Namespaces.Orleans/OrleansResourceOperations.cs` | Seven files scanned, zero findings; parser reports 82.7% parsed lines. Explicitly includes new source files omitted by the tracked-files scan; compiler/analyzer and runtime checks passed independently |

Async host differences are documented: union steps on one head serialize, synchronous
mount mutation fails busy, and admitted provider IO keeps its completion owner after
caller cancellation. The real-grain evidence uses a local process mount table; it
does not claim durable distributed mount-head locking or stream recovery. The
repository-wide `--full` pipeline was not rerun; mutation and fuzz scopes target
the affected namespace and resource-adapter code.

## Native stat and fstat slice (2026-09-24)

`Plan9FileSyscalls.StatAsync` resolves process paths and final mounts without
content opens; `FStatAsync` retains the original open handle and saved visible
name across descriptor duplication, copying and namespace changes. Both return
bounded raw stat records or two-byte size hints, preserving the pinned 9front
`pathlast`/`dirsetname` behavior, including root's empty visible name and separate
provider/name-rewrite retry sizes. Neither changes shared file offsets or consumes
a directory cursor. Record framing, string bounds and rewritten size overflow are
validated before exposing successful metadata.

The typed `FileStatOperations` adapter requires explicit device mappings and
`IResourceOpenStatOperations` for descriptor metadata. Orleans provides the latter
through the optional `IOpenStatResourceGrain` registration. Unsupported capabilities
fail explicitly. Raw providers own temporary protocol fids within their stat
operation; the supplied object/grain adapter allocates none. Admitted stat calls
retain completion ownership after caller cancellation. No durable distributed
recovery, CMSG-aware metadata mutation, or process wstat/fwstat/remove implementation
is claimed by this slice. See [MetadataSyscalls.md](MetadataSyscalls.md) for the
scenario-level evidence and provider boundaries.

Four executable BDD scenarios cover root mounts, retained aliases, short replies
and write-only descriptors. Unit/property tests add malformed replies, Unicode,
exact and minimum sizes, unchanged metadata, current lengths, independently
captured root/current directories, cancellation and close/reuse/exit barriers.
The real-grain test exercises open-handle dispatch, fresh length, path rebinding,
provider post-removal rejection and continued descriptor ownership. The syscall
fuzzer now checks stat framing, name replacement, unchanged metadata strings and
shared descriptor cleanup.

The first mutation run found eight survivors and no timeouts. Boundary/state
assertions were added and redundant validation was removed without new exclusions.
After the machine restart, fresh reports were generated in separate output
directories; interrupted runs are not used as final evidence.
The final reports also verify that mutations within the new open-stat capability
checks compile and are killed. Equivalent type-check expressions replaced pattern
variables that caused Stryker to roll back those methods on compilation errors.

| Command | Result |
| --- | --- |
| `bash scripts/run-quality.sh` | Passed: 1,004 tests across eight projects, including 421 namespace and 118 Orleans tests; zero failed/skipped. Line coverage 84.20%, branch coverage 74.26%, maximum CRAP 30.00; analyzer, gate self-tests and tracked-file Semgrep checks passed |
| From `NinePSharp.Namespaces.Tests`: `dotnet stryker --config-file ../stryker-config-namespaces.json --reporter json --reporter progress --output ../.artifacts/stryker-file-stat-final --skip-version-check --break-on-initial-test-failure --verbosity error` | Passed: 1,102 killed, zero surviving/uncovered/timed-out mutants |
| `python3 tools/mutation_summary.py --output-dir .artifacts/stryker-file-stat-final --min-score 100` | Passed under the strict timeout-rejecting 100% gate |
| From `NinePSharp.Namespaces.Orleans.Tests`: `dotnet stryker --config-file ../stryker-config-orleans.json --reporter json --reporter progress --output ../.artifacts/stryker-file-stat-orleans-final --skip-version-check --break-on-initial-test-failure --verbosity error`, then `python3 tools/mutation_summary.py --output-dir .artifacts/stryker-file-stat-orleans-final --min-score 100` | Passed: 63 killed, zero surviving/uncovered/timed-out mutants |
| `bash scripts/fuzz.sh namespace-syscalls` | Passed: 10 seconds, 1,711 executions, 138 corpus entries, zero crashes/hangs; `.artifacts/fuzz/logs/namespace-syscalls-20260924190001-30664.log` |
| `semgrep --metrics=off --config quality/semgrep.yml --error --no-git-ignore NinePSharp.Namespaces/FileStatOperations.cs NinePSharp.Namespaces/Plan9FileSyscalls.cs NinePSharp.Namespaces/DescriptorGroup.cs NinePSharp.Namespaces/VirtualProcesses.cs NinePSharp.Namespaces.Orleans/OrleansResourceOperations.cs` | Five files, 100% parsed lines, zero findings; includes new files omitted by the tracked-files scan |

NuGet emitted NU1900 because vulnerability metadata was unavailable; these runs
do not establish a fresh dependency vulnerability audit. Repository-wide `--full`
was not run; mutation/fuzz scopes target the affected namespace and Orleans adapter
code. The standard pipeline was rerun after the final implementation/test changes.

## Native wstat and fwstat slice (2026-09-25)

`Plan9FileSyscalls.WStatAsync` and `FWStatAsync` implement raw Plan 9 metadata
mutation. Validation copies and checks the exact record before path lookup or fd
admission, including the pinned source's prefix check and limited sub-64-byte name
check. The descriptor channel retains mount-point state independently of later
mount-table changes, while `ResourceOpenHandle.IsMountTransport` represents the
separate CMSG rejection applied only by fwstat.

`ResourceWStat` preserves native sentinels and `FileStatOperations` forwards one
typed atomic update through `IResourceWStatOperations`. Orleans serializes the
same fields through `ResourceWStatModel` and `IWStatResourceGrain`; a real test
grain verifies combined rename, mode and truncation plus collision rollback.
Caller cancellation is checked before admission and after lookup. Once dispatched,
the syscall owns completion using the authenticated operation identity and an
immutable request copy. Durable reconciliation after a lost distributed reply
remains a separate pending slice.

Four executable BDD scenarios cover validation precedence, all-sentinel dispatch,
mount-point retention and atomic combined changes. Unit/property tests cover exact
field round trips, invalid UTF-8 adaptation, the full 65,537-byte raw boundary,
short-name edge positions, final mounts, CMSG, all open modes, offset preservation,
provider rejection and close/reuse/exit races. The syscall fuzzer now includes
arbitrary wstat framing and a valid-record ownership oracle.

| Command | Result |
| --- | --- |
| `bash scripts/run-quality.sh` | Passed: 1,052 tests across eight projects, including 464 namespace and 123 Orleans tests; zero failed/skipped. Line coverage 84.44%, branch coverage 74.60%, maximum CRAP 30.00; gate self-tests and tracked-file Semgrep passed |
| From `NinePSharp.Namespaces.Tests`: `dotnet stryker --config-file ../stryker-config-namespaces.json --reporter json --reporter progress --output ../.artifacts/stryker-file-wstat-final --skip-version-check --break-on-initial-test-failure --verbosity error`, then the strict mutation summary | Passed: 1,189 killed, zero surviving/uncovered/timed-out mutants; 100.00% |
| From `NinePSharp.Namespaces.Orleans.Tests`: `dotnet stryker --config-file ../stryker-config-orleans.json --reporter json --reporter progress --output ../.artifacts/stryker-file-wstat-orleans --skip-version-check --break-on-initial-test-failure --verbosity error`, then the strict mutation summary | Passed: 67 killed, zero surviving/uncovered/timed-out mutants; 100.00% |
| `bash scripts/fuzz.sh namespace-syscalls` | Passed: 10 seconds, 1,375 executions, 110 corpus entries, zero crashes/hangs; `.artifacts/fuzz/logs/namespace-syscalls-20260925000913-769421.log` |
| Explicit affected-file `semgrep --metrics=off --config quality/semgrep.yml --error --no-git-ignore ...` | Nine files requested, six applicable C# targets scanned, 100% parsed lines, zero findings |

NuGet emitted NU1900 because vulnerability metadata was unavailable. The standard
pipeline, both complete affected mutation configurations, and the bounded syscall
fuzz campaign were run; unrelated mutation and fuzz configurations were not rerun.
