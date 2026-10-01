# Process, descriptor, and fid lifetimes

This is the implementation contract for the remaining lifecycle gap. The native
rules below follow the checked-in manuals and kernel in `../9front`. Durable
ownership, request fencing, and recovery are NinePSharp extensions implementing
those rules across Orleans grains. The accompanying feature files are acceptance
specifications. Traceability.md distinguishes local executable coverage from
remaining integration and recovery work.

## Separate ownership domains

```text
virtual process (pid, incarnation)
  +-- root/current channel references
  +-- namespace-group membership --------> mount table and mount references
  +-- descriptor-group membership -------> fd slots ----+
                                                       |
connection (session epoch)                              v
  +-- fid table --> walked channel / open reference --> open channel
                                                       +-- provider handle
                                                       +-- open mode / ORCLOSE
                                                       +-- file offset / directory cursor
```

| Object | Identity and ownership | Final release |
| --- | --- | --- |
| Process | PID plus durable incarnation; owns its group memberships and root/current references | Explicit termination removes its references; deactivation does not |
| Namespace group (`Pgrp` analogue) | Independent group ID; owns mount references; members can share it | Final member leaves, no prepared transfer remains, and admitted operations drain |
| Descriptor group (`Fgrp` analogue) | Independent group ID; owns numbered slots | Final member leaves, then every remaining slot is detached |
| Descriptor slot | Group ID, fd, allocation generation; owns one open-channel reference and per-slot `OCEXEC` | `close`, destination replacement by `dup`, close-on-exec, or final group release |
| Open channel (`Chan` analogue) | Unique open-instance ID, distinct from file/Qid identity; reference holders include slots and admitted I/O | Final reference releases provider open state; a duplicate does not reopen the file |
| 9P fid | Session epoch, fid number, allocation generation; owns walked-channel state and optional open state | `Tclunk`, `Tremove`, or session teardown/reset |

Mounts, root/current channels, and a future `/srv` publication must retain their
own channel references where applicable. A descriptor count alone is not a
channel reference count. Removing a mount releases its reference, not every open
channel that was reached through that mount.

A numeric fid is never implicitly a process fd. Existing `NamespaceSession`
operations remain the 9P server API. A process fd API resolves a path and opens a
channel into its descriptor group; it does not take ownership of every fid whose
request context happens to contain that PID. A future explicit fd export/import
must specify reference acquisition and authorization separately.

## Native compatibility rules

1. Namespace and descriptor fork choices are independent. Without `RFFDG` or
   `RFCFDG`, `rfork` shares the descriptor table. `RFFDG` copies slots and their
   per-slot flags, retaining the **same** channels. `RFCFDG` creates an empty
   table. The two flags together are invalid. These choices also apply without
   `RFPROC`, replacing the current process's membership. `fork()` selects
   `RFFDG|RFREND|RFPROC`; it must not be equated with default sharing `rfork`.
2. Closing or opening a slot in a shared table is visible to every member.
   Closing a slot in a copied table leaves the other table's slot intact. Exiting
   one member of a shared group does not close the group's descriptors.
3. `dup(oldfd, -1)` chooses the lowest free fd. A specified destination replaces
   that slot after acquiring the source reference. `dup(fd, fd)` retains the
   channel but clears the per-slot `OCEXEC` flag, like other successful dups.
   Invalid sources leave destinations untouched. The destination growth limit
   follows the checked-out `sysfile.c:growfd`: an existing slot below capacity
   is accepted; growth adds 20 slots, rejects a destination at or beyond current
   capacity plus 20, and refuses growth once capacity is at least 5000. Do not
   silently substitute Unix `dup2` semantics.
4. `OCEXEC` is a slot flag: table copy preserves it, `dup` clears it on the
   destination. At a successful exec boundary, 9front closes marked slots in the
   current table, including when that table is shared. Runtime activation,
   Orleans reactivation, and reconnect are not exec. This specifies the future
   application exec hook; it does not require a machine-code exec implementation.
5. Duplicates and copied tables share channel offset and directory iteration
   state; independent opens do not. For regular files, explicit-offset
   `pread`/`pwrite` do not update the channel offset; offset `-1` selects implicit
   offset I/O in 9front. Directory reads have special rewind, union, and offset
   rules in `sysfile.c:read`, so the regular-file rule must not be generalized to
   directories. Sequential implicit I/O advances by the actual transferred
   count. Do not claim stronger concurrent implicit-read atomicity than the
   source provides.
6. `ORCLOSE` belongs to the open channel/provider state and takes effect on final
   close of that open instance, not on the first duplicate fd's close. Distinct
   opens of one file are distinct instances. If a provider rejects removal, no
   detached slot or fid is resurrected. 9front's `cclose` swallows provider close
   errors; native `close(fd)` should not inherit `Tclunk`'s error-return contract.
7. Process exit detaches its resources before releasing them. An already admitted
   operation can hold a channel reference after its fd is closed; subsequent
   lookup of that slot fails. Closure cannot free a channel still held by that
   operation. Process termination does not imply rollback of completed file I/O.
   Final table cleanup and close-on-exec initiate slot releases in ascending fd
   order, following `closefgrp` and the exec loop.
8. 9P zero-element walk clones an **unopened** fid; it is not `dup` of an open fd.
   Partial walks do not install `newfid`, including when `newfid == fid`.
   `Tclunk` and `Tremove` invalidate the addressed fid even on provider error, and
   its number may then be reused. `Tversion` aborts outstanding session I/O and
   clunks active fids. Removal's effect on other open instances remains
   provider-defined, as `remove(5)` expressly allows.
9. Successful `mount(fd, afd, ...)` consumes the source fd slot; failure leaves
   that slot intact. The authentication fd is not consumed. Other references to
   the transport remain reference-owned; duplicate descriptors are not promised
   unrestricted raw I/O after the channel becomes a mount transport.
10. Unmount releases the selected namespace binding's references. It does not
    clunk an already held fid to a regular file inside that mounted tree. With
    the provider still available, that fid can continue I/O or be opened after
    unmount if it was only walked. A new walk from the namespace root follows the
    remaining/replacement bindings. A replacement at the same pathname must not
    redirect a retained fid. This rule does not promise unchanged listings from
    an open union-directory channel: its retained mount head can itself change.

## Orleans ownership and recovery contract

These requirements are distributed extensions, not additional meanings assigned
to native `rfork`, `close`, or 9P messages.

### Durable identities and admission

Persist a schema version and process incarnation with each process, plus its
namespace-group ID, descriptor-group ID, lifecycle state, state version, and
operation journal. Never identify an owner solely by PID, fid number, grain
activation, or a CLR reference count. An operation key combines its durable
issuer epoch and monotonic sequence with a request fingerprint; a repeated key
with different arguments fails without side effects.

Groups store membership tokens as sets, with idempotent acquire/release, and
prepared membership reservations associated with a journal operation. Slots and
channel reference holders likewise have stable allocation tokens. Replaying a
release cannot decrement ownership twice or release a newer occupant of the same
numeric slot. Group states are `Preparing`, `Active`, `Closing`, and `Closed`;
final closure and membership admission are serialized in the owning grain.

Public operations go through the process authority and carry authenticated
principal, process incarnation, and the current membership token. Register an
operation durably before dispatch to another grain. Group/channel admission
validates these tokens and records outstanding operations; a prior validation at
the gateway alone is insufficient. Bare group IDs are not authority. Remove or
restrict unfenced mutation paths when introducing the new interfaces.

### Fork and group replacement

Use a recoverable journal, not an assumed transaction spanning several grain
`WriteStateAsync` calls:

1. Validate flags, authorization, expected process version, and child identity;
   persist the operation intent. Serialize conflicting lifecycle operations.
2. Allocate unique group IDs from this operation identity (not fixed
   `vprocess-<pid>-rfork-copy` names). Record one source snapshot version. Reserve
   destination membership and all copied channel references before publishing
   the destination. A source reservation keeps the snapshot's references alive.
3. Persist a commit decision and the process's new group pointers, or a prepared
   child's complete initial state. The child remains unavailable for application
   calls until that decision and destination memberships are durable. A timeout
   is not an abort decision. Parent exit waits for this recorded decision; a
   committed child survives it, an aborted child is never runnable.
4. Finish membership activation and release old ownership/source reservations
   idempotently. Return completion only after the new ownership is usable. Until
   then, process calls report the pending operation rather than use mixed state.

Recovery finishes a recorded commit or releases a recorded abort's reservations.
If the coordinator is unavailable or the outcome is unknown, retain prepared
references and report recovery pending. Never guess from elapsed time. A repeated
call returns the same child/groups/result. Ordinary create of an occupied child
identity conflicts. No committed process can refer to an unowned group.

### Termination and connection cleanup

The first durable transition `Active -> Terminating` is the process admission
cutoff. Persist an exit operation containing the memberships, root/current
references, process-bound sessions, and unfinished operation records to release.
Reject subsequent opens, attaches to that incarnation, forks, directory changes,
and namespace mutations. Retry of the same exit returns its existing status.

Mark each group membership and process-bound session as closing and obtain drain
acknowledgements. Work durably admitted before the cutoff may complete; retain
its reference until completion or acknowledged cancellation. A late open/create
result cannot publish a new fd or fid into a closing owner: record its returned
handle for cleanup. A failed reply does not prove the provider did nothing.

Release only this process's memberships. Final descriptor-group release detaches
all its slots and queues their reference releases. Final namespace-group release
closes its table and queues mount-reference releases. Root/current references
are released independently. Group members belonging to surviving processes remain
valid, including channels whose creator PID was the exiting process.

Persist `Terminated` only after ownership releases and drains are durably
accounted for. Provider cleanup may remain pending in the durable cleanup ledger;
status exposes `cleanup-pending`, counts, and errors separately from process
termination. Status must not imply that remote resources are gone while a
provider is unreachable. Durable reminders/reconciliation resume journals and
cleanup after activation or silo loss, without requiring the client to retry.

A connection close/reset releases that session's fids. It does not terminate a
durable application process or discard its fd group. For sessions explicitly
bound to a process incarnation, exit fences and closes those sessions; an
administrative connection controlling that process has its own identity and is
not terminated with it. A fresh connection receives a new epoch and no old fids.
An explicitly negotiated retained-session transport can reconnect the same
logical session only under its own authenticated resume contract.

### Provider boundary and failure guarantees

Before open/create, persist a unique operation and prospective owner. The
provider commits handle creation and its operation result together; retry returns
the same handle. Before final close, persist the close intent, reference token,
and stable operation ID. Retry that ID until its outcome is known. Handle-close
deduplication must survive provider reactivation and be atomic with the close
effect; the existing interface comment alone is not evidence of this guarantee.

Process-bound authorization must not make shared handles depend on the original
creator's continued existence. Validate the current holder, and authorize a
bounded cleanup capability for previously issued handles after process exit.
Fencing must cover reads as well as mutations; current read signatures do not
yet carry all required context.

Transient failure leaves a retryable cleanup record. A definitive provider close
error leaves a terminal failure record for reconciliation; never restore local
ownership. Late replies carry allocation tokens and cannot change a reused fd
or fid. Cancellation releases no remote reference until the operation outcome is
known. Arbitrary external side effects cannot be made exactly-once by Orleans
alone: providers must supply atomic deduplication or declare the operation
non-recoverable. A non-recoverable handle is invalidated with an explicit
handle-lost outcome after recovery; never reopen it by pathname as a substitute.

Retain closed process/group and released-owner tombstones while messages can
still be replayed. Initial implementation does not recycle group IDs or process
incarnations and retains tombstones. Future compaction requires durable fencing
watermarks that reject all retired epochs, not only a time-to-live.

Legacy snapshots have no authoritative owner sets. Migrate with process/group
mutations quiesced, enumerate durable process records, reconstruct memberships,
and persist a migration completion marker before enabling final-owner cleanup.
Abort migration if the store cannot provide a complete inventory. Do not infer
that an unregistered legacy group has no owners and delete it. Existing snapshots
have no process fd table; those processes start with an explicitly empty table.

## Implementation and verification boundary

The local `DescriptorGroup` now owns slot references independently of
`VProcessGroup`. `DescriptorLease` retains an admitted operation's reference;
share/copy/empty inheritance and process exit release are integrated into
`VProcessTable`. The provider-open installation API transfers ownership only on
success; it does not yet perform path-based open/create or store shared offsets.
`CloseOnExecAsync` is an explicit hook, not an implementation of application exec.

Next extend process snapshots/contracts
with incarnation, fd-group membership, and lifecycle journals; add durable group
membership and channel ownership; finally connect gateway session admission and
provider recovery. The old synchronous local `Terminate` can detach local
ownership, but distributed termination requires an operation/status result.

`DescriptorLifetimes.feature` specifies native behavior; `DistributedLifetimes.feature`
specifies recovery behavior. `DescriptorGroupTests` and executable
`Features/DescriptorLifetimes.feature` cover the local ownership layer. Bind the
remaining behavior to syscall fixtures and persistent Orleans test silos plus
gateway/provider fault fixtures. Until those bindings exist, keep their
traceability status **specified, pending**.

Property tests must generate independent namespace/fd fork modes and arbitrary
close/dup/exit sequences against a reference ownership model. Check that live
references equal slot, mount, root/current, session, prepared, and admitted-I/O
holders; zero owners prohibit admission; and each final close intent has one
stable identity. Fuzz the same model with reused numeric IDs and invalid flags.
Inject failure before and after every durable write and cross-grain reply in
fork, replacement, exit, open, and final close. Compare recovered observable state
with uninterrupted execution. Run the standard quality gate, bounded fuzzing,
and relevant mutation scopes at 100%, without new exclusions, before marking
implementation coverage complete. Documentation parsing does not satisfy these
runtime gates.

## Source mapping

Paths below are relative to the sibling `../9front` checkout from the repository
root. Both manuals and named functions were read when specifying these rules.

| Rules | Manuals | Implementation |
| --- | --- | --- |
| Independent groups, share/copy/empty, no-child rfork | `sys/man/2/fork` | `sys/src/9/port/sysproc.c:sysrfork`; `pgrp.c:dupfgrp`, `closefgrp`, `closepgrp` |
| Duplication, flags, slot closure | `sys/man/2/dup`, `sys/man/2/open` | `sysfile.c:newfd`, `growfd`, `sysdup`, `fdtochan`, `fdclose`; `sysproc.c:sysexec` |
| Offsets and directory exceptions | `sys/man/2/read`, `sys/man/2/seek` | `sysfile.c:read`, `write`, `syspread`, `syspwrite`, `unionread` |
| Exit and final channel close | `sys/man/2/exits`, `sys/man/2/open` | `proc.c:pexit`; `pgrp.c:closefgrp`; `chan.c:cclose` |
| Fid clone, invalidation, reset, removal | `sys/man/5/walk`, `clunk`, `remove`, `version` | Client channel release in `chan.c:cclose`; server fid behavior is the protocol contract, not `Fgrp` |
| Mount source fd consumption | `sys/man/2/bind` | `sysfile.c:bindmount` |
| Retained file fids across unmount | `sys/man/2/bind`, `sys/man/5/walk`, `clunk` | `chan.c:cunmount`, `cclose`; `pgrp.c:mountfree` releases mount references rather than all references to the resource |

The `dup(2)` wording refers to the highest descriptor ever used; `growfd` checks
allocated table capacity instead. At this discrepancy the target is the
checked-out 9front implementation; regression cases must exercise capacity
boundaries and table copying (which can shrink capacity). Distributed recovery
has no kernel equivalent.
