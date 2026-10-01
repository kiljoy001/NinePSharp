# Native directory streaming contract

[DirectoryStreaming.feature](DirectoryStreaming.feature) expands `NS_IO_011` into
44 individually tagged BDD scenarios. The implementation and evidence mapping below
distinguishes executable coverage from remaining distributed work. The executable
[metadata cursor profile](DirectoryCursors.md) remains a separate adapter with
known differences. The host now explicitly selects metadata or provider-stream reads;
legacy providers are not silently treated as native byte-stream providers.

## Reference and precedence

Compared on 2026-09-16 with the sibling `../9front` checkout at
`9654fe7fa882f8043c267bbeb6679ebd9102209b`. The consulted files had no local changes.

| Reference | Contract used |
| --- | --- |
| `sys/man/5/read`, `sys/man/5/stat`, `sys/man/2/dirread` | Complete variable-length stat records, byte counts, sequential provider offsets, encoding and directory read behavior |
| `sys/man/2/read`, `sys/src/9/port/sysfile.c:read`, `syspread` | Implicit offset sentinel, directory pread, rewinding at zero and separate outer counters |
| `sys/man/2/seek`, `sysfile.c:sseek` | Directory seek restrictions and deferred stream reset |
| `sys/man/2/bind`, `sysfile.c:unionread`, `unionrewind` | Ordered unions, active member handles, component failures and rewind |
| `sysfile.c:dirfixed`, `dirname`, `dirsetname`, `mountfix` | Mount identity lookup, replacement stat and original-name preservation |
| `sysfile.c:mountrock`, `mountrockread`, `mountrewind` | Whole-record overflow storage, drain priority and reset |
| `chan.c:namec` (`Aopen`), `cclone`, `cmount`, `cunmount`, `findmount`, `eqchantdqid` | Initial open, retained multiple-member head, live list changes and caller namespace lookup |
| `sys/man/2/dup`, `sysfile.c:sysdup`, `fdtochan`, `fdclose`; `pgrp.c:dupfgrp` | Shared open-channel ownership and admitted-operation references |
| `sys/man/5/clunk`, `chan.c:cclose`, `chanfree`; `pgrp.c:closepgrp` | Final cleanup, close errors, retained heads after namespace removal |

For conflicts or underspecified behavior, the pinned kernel implementation governs
the `@native` scenarios. In particular, `seek(2)` prohibits directory seeks generally,
while `sseek` permits absolute zero. `read(5)` specifies legal offsets at the **provider
protocol boundary**; the syscall layer has additional union/overflow branches that
do not always validate a positive supplied offset. These are separate contracts.

`@async_adaptation` cases explicitly add host ordering, cancellation and completion
ownership rules. `@provider_contract` specifies defensive validation at the managed
adapter boundary; it does not claim that native C validates all malicious buffers.
Neither category may be cited as behavior directly implemented by the 9front kernel.

## State and provider boundary

One open directory channel owns:

- Its initially opened outer provider handle and visible byte position (`offset`).
- The outer device counter (`devoffset`), which is not always the number of bytes
  actually fetched from the provider; see overflow accounting below.
- An optional retained union mount head (`umh`), positional member index (`uri`),
  and independently opened active member (`umc`) with its own provider byte offset.
- Complete original stat records in the mount overflow buffer (`dirrock`).

Dup and copied descriptor tables share this state, including the active member
and overflow. Independent opens acquire independent state. Copying a namespace
group does not change an already open channel's retained union head. Rewriting
entries nevertheless uses the **calling process's** current namespace through
`findmount`, not a mount table permanently captured from the opener.

The stream-capable provider boundary must support retained handle identity,
bounded reads with explicit provider offsets, clone/open/close for union members,
and stat on a selected mounted resource. Providers supply whole stat records.
Transport implementations must respect their negotiated transfer limits without
splitting a record returned to the syscall layer. A whole-list metadata enumeration
is not sufficient evidence for this contract. Legacy metadata adapters can remain
explicitly identified as such; this specification does not require removing them.

Mount identity requires the equivalent of native device type, device instance and
Qid path. `eqchantdqid(..., 1)` ignores Qid version (and does not compare Qid type).
A provider adapter therefore needs trustworthy identity mapping; matching a visible
name, a versioned Qid alone, or zeroed type/dev fields is insufficient. Internal
device indexes and encoded device types must be translated consistently, as native
`dirfixed` uses `devno` before `findmount`.

## Read and rewind ordering

The source executes these steps for an admitted directory read:

1. Resolve the implicit offset (`pread` minus one) or supplied offset; reject other
   negative offsets before reading anything.
2. At effective offset zero, reset both outer counters, discard overflow, reset the
   member index and release the active union member.
3. Try copying complete records from overflow. If none fit, continue to the next
   branch; having pending overflow alone does not stop the read.
4. If a retained union head exists, run `unionread`. Otherwise validate supplied
   offset against the visible position and read the outer provider at `devoffset`.
5. Apply `mountfix` to the raw batch, including batches taken from overflow.
6. On successful completion, add the raw batch length to `devoffset` and the final
   returned length to `offset`, even for explicit directory pread.

There is no native early return for count zero. A zero-count provider read can
return zero; inside `unionread` that is also a reason to close/advance a component.
The metadata profile's zero-count no-provider-call optimization is not native parity.

`sseek(fd, 0, 0)` sets visible offset, `uri` and `dri` to zero. It does **not** call
`unionrewind` or `mountrewind`, clear `devoffset`, or immediately clunk `umc`.
The following read at zero performs those actions. The current metadata adapter's
eager reset is a documented difference. Tests must observe close timing, not merely
that subsequent enumeration starts at the beginning.

## Union traversal and changing mount lists

Initial open uses the first resolved provider and can fail; enumeration-time
component skipping does not rescue that failure. `namec:Aopen` saves a mount head
only when it has multiple members at open time. It opens the outer channel normally.
Later `unionread` independently clones and opens members with `OREAD`; the first
member's enumeration handle is not the outer handle.

Within a union read, `umqlock` serializes member traversal and the mount-head read
lock protects the current list across clone/open/read and release of failed or
exhausted members. Empty/failed members are skipped in the same call. A nonempty
batch returns immediately; the kernel does not fill the remainder from another
member. Successfully acquired clone ownership must be released after failed open
as well as after read failure/EOF. A definite component error may therefore become
successful data from a later member or zero after all members are exhausted.

Between calls the kernel seeks the current mount list by numeric `uri`. It does
not reconcile that position with the identity of retained `umc`. Append, prepend,
selected unmount and replacement can consequently cause omissions or repeated
visits. Scenarios `NS_DIR_018`–`NS_DIR_022` spell out deterministic examples;
stable snapshots or exactly-once names would be incompatible claims here.

Complete unmount detaches and empties the old head. An existing descriptor still
owns that head, and a later mount at the same path creates another head. Without
overflow, the old descriptor returns zero when its retained head has no member at
`uri`; it need not immediately release an already retained `umc`. Rewind/final
release does so. In-place MREPL mutates an existing head instead of providing this
detach-and-new-head behavior. `closepgrp` also empties heads before dropping its
references; an open stream reference does not freeze the namespace's member list.

## Mount rewriting and source quirks

`mountfix` stats the first mounted target but retains the original directory entry
name. If the original identity is any member of that mount's union, it skips the
rewrite. A failed or insufficient replacement stat falls back to the original
record; acquired temporary references still release. The initial stat scratch
buffer is 4096 bytes and can be retried with additional room for the reported size
and original name. Provider metadata and encoded names, including multibyte UTF-8,
must agree with the final record/string length fields.

The following source behavior must not be silently replaced with a nicer contract:

- **Overflow order:** expanding a record evicts trailing original records one at
  a time, appending each to `dirrock`. For a batch A,B,C needing both tails evicted,
  overflow is C,B. Preservation of member traversal order does not imply preservation
  of every record's original order across this buffering case.
- **Overflow accounting:** `read` adds `nn` to outer `devoffset` even when `nn` came
  from `dirrock` rather than a provider. In `NS_DIR_029`, raw 120 bytes become visible
  100; returning the buffered 60 later leaves device counter 180 and visible counter
  160. The next non-union provider read uses that device counter. Active union-member
  offsets advance only on their own device reads. Do not relabel `devoffset` as a
  strictly physical fetched-byte counter or silently correct this pinned behavior.
- **A zero result need not mean all state is exhausted:** if the replacement for
  the current record cannot fit even after evictions, that original record can be
  buffered and this call returns zero. If visible position is still zero, the next
  implicit read rewinds and discards it. General no-progress avoidance or a uniform
  small-buffer exception would be an intentional deviation, not native evidence.
- **Overflow priority:** buffered records are reprocessed by current `mountfix` and
  may be returned even after complete union unmount. A positive, mismatched pread
  offset can also succeed on this branch. If no whole buffered record fits,
  `mountrockread` returns false and the kernel can proceed to provider IO.

Native error handling is not a transaction over all directory state. An error
after a member read may leave the member offset advanced even though the outer
offset update is not reached. Provider failures, malformed bytes and uncertain
completion must not be handled by blindly replaying the last read.

These quirks are intentional compatibility fixtures against the pinned source,
not recommendations for new protocols. If a later implementation deliberately
deviates, it must change the classification and explain the divergence; the old
metadata adapter is not a substitute oracle.

## Async ownership and cleanup adaptation

An admitted operation owns a channel lease independently of descriptor number.
It also owns every successfully acquired member handle until ownership transfers
to retained channel state or cleanup. Closing/reusing an fd or exiting a process
cannot redirect that operation. Final release eventually closes outer and member
handles, frees overflow and drops retained mount-head references. Native `cclose`
can queue some device closes, so acceptance checks require eventual release rather
than synchronous network clunk at descriptor detachment. An acknowledged clunk error
still invalidates the fid and does not suppress remaining local cleanup.

Fog serializes a whole directory read/rewind transition through an asynchronous
cursor lease. This is stronger than 9front's separate member, rock and offset locks;
it is not a claim that native `read` and `seek` are one atomic operation. Waiting
callers can cancel before admission without side effects. Once provider IO is
admitted, cancellation must not discard knowledge of its outcome or a returned
handle. A late open remains owned even when the last descriptor has closed.

To preserve the native exclusion between union IO and same-head mount mutation,
use an awaitable head read lease (or an equivalent explicit mutation barrier),
with exclusive head mutation queued until that union step completes. Do not hold
monitor/process/descriptor-table locks across provider awaits. Operations on
unrelated heads remain possible. A cursor snapshot followed by immediate same-head
mutation would not preserve this ordering. The head lease covers the union step;
it does not freeze all namespaces throughout later `mountfix` stat calls.

Caller cancellation is distinct from an acknowledged native component error.
The former must not silently skip a member whose open/read may have completed.
`NS_DIR_042` requires completion ownership and resolution before unsafe progress;
durable reactivation, reconciliation and crash recovery remain the separate
[DistributedLifetimes.feature](DistributedLifetimes.feature) contract. Implementing
local streaming alone must not mark those distributed cases covered.

## Acceptance and implementation slices

| Slice | Scenarios | Evidence required before completion |
| --- | --- | --- |
| Provider stream and shared offsets | `NS_DIR_001`–`NS_DIR_010`, `NS_DIR_016`, `NS_DIR_044` | Handle/offset/count traces, stat framing, independent opens, shared descriptors, seek timing and protocol error fixtures |
| Union streams and live membership | `NS_DIR_011`–`NS_DIR_022` | Separate outer/member handles, injected clone/open/read failures, close accounting and deterministic between-read mount changes |
| Mountfix and overflow | `NS_DIR_023`–`NS_DIR_035` | Identity-aware stat fixtures, exact raw/visible byte counts, overflow ordering and namespace changes between reads |
| Cleanup and async adaptation | `NS_DIR_036`–`NS_DIR_043` | Completion barriers at member open/read/close, queued rewind/mutation, exit/reuse, exactly-once local release and uncertainty classification |

Executable bindings should exercise the production stream API, including at least
one real Orleans provider, without delegating all assertions to a test-only model.
Property tests should generate valid record lengths, batches, duplication/close
sequences and rewinds, checking framing and ownership against an independent model.
Order properties must account for native mountrock tail eviction and live membership
changes rather than asserting universally stable enumeration.

Extend the namespace syscall fuzz target with raw-record validation, streaming
offsets, union transitions and overflow replay. Run the standard quality pipeline,
the relevant mutation scope with zero surviving/uncovered mutants, and bounded fuzz
campaigns. Report mutation timeouts separately. These gates are future implementation
acceptance requirements; parsing this design feature is not behavioral validation.

## Specification validation

On 2026-09-16 the cached Gherkin 29.0.0 parser accepted all 44 scenarios (54 cases
after expanding example tables) and the updated parent `ProcessFileSyscalls.feature`.
Scenario identifiers were checked for uniqueness and continuity, classifications
for 37 native, six async-adaptation and one provider-contract scenario, and document
links for valid local targets. `git diff --check` passed. Runtime tests, mutation
testing and fuzzing were not rerun for this documentation-only change.

## Implementation evidence (2026-09-16)

`Plan9FileSyscalls` accepts `DirectoryReadMode.ProviderStream`. The default remains
`Metadata` for existing providers. Native reads use `INamespaceDataPlane.ReadAsync`
on the retained open handle. Providers must return bounded complete 9P2000 records;
the syscall validates framing, including the full unsigned wire-size prefix range.
Stat rewriting additionally requires `IDirectoryStatOperations`. The supplied
`DirectoryStatOperations` adapts typed resource metadata using explicit unique
`DirectoryDeviceBinding` mappings for type/device/provider identity. Wire providers
must encode matching identities. No mapping is guessed from a filename or Qid alone.

The stream owns lazy union-member handles, the separate counters and mount overflow.
It follows live retained-head membership, including the positional omissions/repeats
specified above. Complete unmount detaches the old head. `NamespaceSyscalls` uses
the new awaitable `MountTable.MountAsync`/`UnmountAsync` paths. Direct synchronous
mount mutations remain available when the head is idle; while it is leased they
throw `NamespaceMutationBusyException` instead of blocking a scheduler thread.
This host API distinction is an explicit async adaptation. The current head gate
also serializes different streams' union steps on the same head; native readers can
share the head's read lock. Other mount heads remain independent.

Admission captures the caller's namespace with the descriptor lease. Waiter
cancellation cannot mutate cursor state; once a provider operation starts, the
adapter awaits its result without abandoning a possibly acquired handle. Providers
report acknowledged directory rejections with `ResourceDirectoryRejectedException`;
Orleans uses `ResourceDirectoryRejectedGrainException` across the grain boundary.
Provider `OpenAsync` is the atomic clone/open abstraction: if it rejects, it owns
cleanup of any internal clone and returns no owned handle. The engine closes every
successfully returned member handle after EOF/failure/rewind/final release.

Unknown or malformed read outcomes poison the cursor against unsafe replay. Unknown
member-open outcomes surface `DirectoryOperationUncertainException` with the original
operation context. This identifies the operation for the host; **automatic durable
reconciliation and recovery of an unreturned handle are not implemented**. Thus
`NS_DIR_042` has partial local evidence and remains part of the distributed lifecycle
work. Providers and hosts must not relabel transport uncertainty as a definite skip.

| Design cases | Executable evidence |
| --- | --- |
| `001`–`010`, `016` | `DirectoryStreamingTests`: provider offsets, shared/copy/independent cursors, pread, zero-count, failed reads and seek; executable `DirectoryStreaming.feature` adds bounded read and zero-count cases |
| `011`–`015`, `017`–`022` | Lazy union open/read/close, definite open/read failure, initial-open failure, append after EOF, selected unmount, prepend, replacement and complete unmount regression tests |
| `023`–`028` | Name/UTF-8 and metadata fidelity, identity/version handling, union original suppression, failed/short stat, stat resize retry and caller namespace tests |
| `029`–`035` | Exact raw/visible byte accounting, reverse tail eviction, zero-result overflow, remount before drain, pread priority, undersized overflow fallback and unmounted-head overflow tests |
| `036`–`041`, `043` | Shared references, deferred rewind, provider close errors, cursor cancellation, head-mutation barriers, descriptor reuse, late member open after exit; native union fuzz checks successful-open/final-close balance |
| `042` | Unknown open is not skipped or replayed and retains its operation context in the error; durable outcome resolution and cleanup remain pending |
| `044` | `DirectoryWireTests` and malformed-provider tests validate record/string bounds, count, trailing data and unknown-outcome handling |

The five executable Gherkin scenarios live in
[`NinePSharp.Namespaces.Tests/Features/DirectoryStreaming.feature`](../../../NinePSharp.Namespaces.Tests/Features/DirectoryStreaming.feature).
The 44-case design file is not automatically bound wholesale. FsCheck and the
`namespace-syscalls` fuzz target exercise the production streaming API. The real
Orleans integration test verifies raw directory reads, rejection serialization,
new entries after EOF and rewind against resource grains with a local process
mount table. It does not establish durable cross-grain mount-head locking or
stream-state recovery. See [FileSyscallValidation.md](FileSyscallValidation.md) for
validation commands and outcomes.
