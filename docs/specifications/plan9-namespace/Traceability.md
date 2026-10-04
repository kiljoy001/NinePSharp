# Plan 9 namespace specification traceability

This document records why each feature exists and whether it is already covered by the current NinePSharp implementation.

| Feature or scenario range | 9front/manual reference | Current NinePSharp status | Classification |
| --- | --- | --- | --- |
| `NS_CORE_001`–`NS_CORE_004` | `chan.c:cmount`, `chan.c:createdir`, `sys/man/2/bind` | `MountTable` and `NamespaceNavigator` implement replacement, ordered unions, and create selection | Covered at the model level |
| `NS_CORE_005` | `chan.c:cmount` directory-only union checks | `MountTable.ValidateMount` rejects non-directory unions | Covered |
| `NS_CORE_006` | `chan.c:walk`, `domount`, `undomount`, `Path.mtpt[]` | `NamespaceChannel` retains simplified traversal frames and supports the demonstrated mount crossing | Partial; nested and `mchan` cases remain |
| `NS_CORE_007`–`NS_CORE_010` | `chan.c`, `sysfile.c:unionread`, `cunmount` | Open channels resolve the current mount head; union order, bind copying, and selected unmount are implemented | Covered for represented identities |
| `NS_CORE_011` | `chan.c:findmount` compares device/type/Qid identity | `MountTable` keys by `ResourceIdentity` | Covered for the current identity contract; device/Qid fields need expansion for full parity |
| `NS_CORE_012` | `chan.c:walk` performs work on a clone and updates the caller after success | `NamespaceNavigator.WalkAsync` returns a cloned channel and successful Qids | Covered |
| `NS_CORE_013` | `chan.c:cmount` rejects `MCREATE` binds that copy multiple or non-creatable source members | `MountTable.ValidateSourceMounts` enforces the restriction | Covered |
| `NS_CORE_014` | `sysfile.c:unionread` returns directory entries in union order without name de-duplication | `NamespaceNavigator.ReadDirectoryAsync` concatenates member entries | Covered |
| `NS_CORE_015` | `chan.c:createdir` returns `Enocreate` when no member has `MCREATE` | `MountTable.SelectCreateTarget` raises `CreateNotPermitted` | Covered |
| `NS_PROC_001`–`NS_PROC_004` | `sysproc.c`, `sysfile.c:syschdir` | `VProcessTable`, `VProcess`, and Orleans process grains implement root/current channels and share/copy/empty groups | Covered at the virtual-process level |
| `NS_PROC_005` | `pgrp.c:closepgrp`, `proc.c:pexit` | Local `VProcessTable.Terminate` releases process channels and namespace ownership; the last owner closes and empties the mount table; stale namespace operations fail | Covered locally, including descriptor-group ownership release; durable Orleans ownership, process-bound session drain, and provider cleanup recovery remain |
| `NS_PROC_011` | `pgrp.c:closepgrp` returns while references remain | Shared children retain their ownership after parent termination; copied groups close independently | Covered locally by `VirtualProcesses.feature` and lifecycle property tests |
| `NS_PROC_006`–`NS_PROC_007` | `sysproc.c:RFNOMNT`, `dev.c:canmount`, `Pgrp.notallowed` | `MountTable` persists blocked device names and a namespace-wide mount-disabled flag; copied no-mount processes carry the restriction | Covered for the current policy model; device-table parity remains |
| `NS_PROC_008` | `pgrp.c:pgrpcpy` | Clone preserves relative member order but rewrites IDs locally | Covered behaviorally; mount-ID parity is incomplete |
| `NS_PROC_009` | `pgrp.c`, process persistence is outside the kernel namespace algorithm | Orleans grains persist process and mount snapshots | Covered by the Orleans adapter contract |
| `NS_SYS_001` | `sys/man/2/bind`, `sysfile.c:bindmount` evaluates source and target before `cmount` | `NamespaceSyscalls.BindAsync` resolves both paths before mutating the process group | Covered for the virtual-process adapter |
| `NS_SYS_002` | `sys/man/2/bind`, `chan.c:cmount` | `MountTable.ValidateMount` checks directory/file compatibility | Covered |
| `NS_SYS_003`–`NS_SYS_006` | `sys/man/2/bind`, `sysfile.c:bindmount` | `NamespaceSyscalls.MountAsync` validates read-write mode and authentication, carries `aname/spec`, and closes the source callback after success | Covered for the virtual-process adapter; transport fd ownership remains provider-owned |
| `NS_SYS_007` | `sys/man/2/bind`, `sysproc.c` | Shared and copied virtual process groups observe the required mount behavior | Covered at the virtual-process level |
| `NS_SYS_008`–`NS_SYS_009` | `sys/man/2/bind`, `sysfile.c:sysunmount` | Complete unmount and invalid ordering flags are implemented by the mount table | Covered at the model level |
| `NS_SYS_010` | `sys/man/2/bind`: `MCACHE` is valid for `mount`, not `bind` | The channel-based bind overload rejects `MountFlags.Cache`; the service-mount overload accepts it | Covered at the API boundary; cache policy remains provider-owned |
| `NS_SYS_011` | `sys/man/2/bind`: a service mount's `old` target is a directory | The service-mount overload rejects replacement onto a regular-file target | Covered at the API boundary |
| `NS_PROC_010` | `sysproc.c`: `RFNAMEG`/`RFCNAMEG` also operate without `RFPROC` | `VProcessTable.RforkNamespace` and `IVProcessGrain.RforkNamespaceAsync` replace the current process group without creating a child | Covered for namespace groups; other rfork groups remain |
| `NS_CTL_001`–`NS_CTL_008` | No native 9P equivalent; native `bind`/`mount`/`unmount` are syscalls in `sys/man/2/bind` | `NamespaceControlResource` exposes synchronous `bind`, `unmount`, `rfork`, `mounts-disabled`, and `kill` commands plus `status` and `ns`; operation IDs, asynchronous reply/status, version conflicts, principal isolation, and reconnect recovery remain | Partial NinePSharp distributed extension; existing ctl does not satisfy the complete scenarios |
| `NS_DEV_001` | `devproc.c:readns1` | `/proc/<pid>/ns` exists, but emits diagnostic root/cwd and mount identities rather than native replayable bind/mount commands; reconstruction and global mount-allocation ordering remain unverified | Partial |
| `NS_DEV_002` | `devsrv.c`, `sys/man/3/srv` | Current Orleans resource registration is not a `/srv` descriptor registry | Distributed projection extension |
| `NS_DEV_003`–`NS_DEV_004` | `devshr.c`, `sys/man/3/shr` | No global shared mount registry currently exists | Missing |
| `NS_DEV_005` | `sys/man/4/namespace`, ordinary 9P provider behavior | Distributed resource grains already participate in ordinary walk/read operations | Covered for mounted providers |
| `NS_ASYNC_001`–`NS_ASYNC_008` | No CSP primitive in Plan 9 namespace semantics | No message-channel grain/resource currently exists | NinePSharp application extension |
| `NS_FID_001` | `sys/man/2/bind`, `sys/man/5/walk`, `clunk`; `chan.c:cunmount`, `cclose`; `pgrp.c:mountfree` | `FidUnmount.feature` executes 12 cases: retained open/unopened file fids survive selected/complete unmount and replacement, fresh walks select the current tree, clunk/disconnect releases the retained handle | Covered locally; provider remains available, no claim about unchanged union-directory iteration or distributed recovery |
| `NS_FD_001`–`NS_FD_006` | `sys/man/2/fork`; `sysproc.c:sysrfork`; `pgrp.c:dupfgrp`, `closefgrp`; `proc.c:pexit` | `DescriptorGroup` and `VProcessTable` implement independent share/copy/empty inheritance, membership replacement, and final-owner release | Covered locally by `DescriptorGroupTests`, executable `DescriptorLifetimes.feature`, and generated lifecycle tests; raw rfork flag decoding remains pending |
| `NS_FD_007`–`NS_FD_010` | `sys/man/2/dup`; `sysfile.c:sysdup`, `newfd`, `growfd` | `DuplicateAsync` retains source references, replaces destination slots, clears OCEXEC, and follows 9front capacity boundaries including copy shrinkage | Covered locally by `DescriptorGroupTests`; manual/code discrepancy documented |
| `NS_FD_011`–`NS_FD_014` | `sys/man/2/read`, `open`; `sysfile.c:read`, `write`, `fdtochan`, `fdclose`; `chan.c:cclose` | Reference leases, final-close callback ownership, swallowed local close errors, regular-file offsets and provider-stream directory cleanup are covered by descriptor and syscall tests; ORCLOSE execution remains provider-owned | Partial: end-to-end ORCLOSE provider tests and durable cleanup remain pending |
| `NS_FD_015`–`NS_FD_017` | `sys/man/2/open`, `dup`, `bind`; `sysproc.c:sysexec`; `sysfile.c:bindmount` | Table copies preserve OCEXEC and `CloseOnExecAsync` closes marked slots in the current shared table | Local exec hook tested; application exec boundary and actual mount-fd integration remain pending |
| `NS_LIFE_001`–`NS_LIFE_018` | No native durable-grain equivalent; preserves ownership rules of `pgrp.c` and `proc.c:pexit` | `ResourceLifetimes.md` and `DistributedLifetimes.feature` define durable membership, transfer journals, termination fencing, session separation, provider cleanup, and legacy migration | NinePSharp distributed extension; specified, pending implementation and executable bindings |
| `NS_IO_001`, `NS_IO_002`, `NS_IO_006`–`NS_IO_009` | `sys/man/2/open`, `read`; `sysfile.c:sysopen`, `fdtochan`, `read`, `write`; `chan.c:Aopen` | `Plan9FileSyscalls` resolves process paths, strips OCEXEC before provider open, installs lowest free fd, enforces access, and implements implicit/positioned regular-file IO including the -1 sentinel | Covered by syscall unit/property tests and executable shared/positioned/failed-write scenarios; provider permissions and truncation remain provider responsibilities |
| `NS_IO_003`–`NS_IO_005` | `sys/man/2/open`, `sys/man/5/open`; `chan.c:Acreate`; `sysfile.c:syscreate` | `Plan9CreateRequest` retains OEXCL in an integer mode; `CreateAsync` walks the visible name, opens existing files with OTRUNC, or calls atomic create(5) in the MCREATE member. Ordinary create retries lookup after a definite rejection; OEXCL never does | Covered by BDD, unit/property tests, competing-create barriers, allocation/exit cleanup tests, create fuzz model, and a real Orleans resource-grain rejection round trip. Permissions, atomic name creation and durable provider commits remain provider responsibilities |
| `NS_IO_011` | `sysfile.c:read`, `unionread`, `mountfix`; `sys/man/5/read` | Explicit `ProviderStream` mode reads retained handles, tracks separate counters, streams union members, rewrites mounted stat records and buffers overflow; default metadata mode remains separate | Local native streaming implemented with BDD/unit/property/fuzz and real Orleans-resource evidence; identity mappings and conforming raw providers required; durable distributed stream ownership remains pending |
| `NS_DIR_001`–`NS_DIR_010`, `NS_DIR_016` | `sys/man/5/read`, `sys/man/2/read`, `seek`, `dirread`; `sysfile.c:read`, `syspread`, `sseek`; `pgrp.c:dupfgrp` | Provider streams, shared positions, pread, zero-count IO and deferred rewind | Executable regression/property coverage and bounded/shared/zero-count BDD; metadata cursor remains a different profile |
| `NS_DIR_011`–`NS_DIR_015`, `NS_DIR_017`–`NS_DIR_022` | `chan.c:namec`, `cmount`, `cunmount`; `sysfile.c:unionread`, `unionrewind`; `sys/man/2/bind` | Lazy member streams, definite failure skipping, initial-open distinction, retained heads and live positional traversal | Local regression coverage; provider OpenAsync abstracts atomic clone/open and owns failed internal-clone cleanup |
| `NS_DIR_023`–`NS_DIR_035` | `sysfile.c:mountfix`, `mountrock`, `mountrockread`, `read`; `chan.c:findmount`, `eqchantdqid`; `sys/man/5/stat` | Explicit identity/stat adapter, caller namespace, overflow tail order and counter accounting, stat retry and small-buffer cases | Executable local coverage, including source quirks; production providers must supply consistent wire identity mappings |
| `NS_DIR_036`–`NS_DIR_037` | `chan.c:cclose`, `chanfree`; `sysfile.c:fdtochan`, `fdclose`; `sys/man/5/clunk` | Final outer/member release, buffer clearing, retained-head retirement and acknowledged close errors | Local lifecycle tests and fuzz handle-balance checks; durable unknown-close recovery remains outside this slice |
| `NS_DIR_038`–`NS_DIR_043` | Async adaptation of retained references and `unionread` head locking; `DistributedLifetimes.feature` for uncertain outcomes | Awaitable cursor/head gates, cancelled waiters, close/reuse/exit and late-open cleanup | Barrier tests cover local ordering; head gate also serializes readers, sync mutation fails busy; NS_DIR_042 prevents replay and preserves the unknown operation identity but durable resolution remains pending |
| `NS_DIR_044` | `sys/man/5/read`, `stat` record contract; managed provider validation policy | Reject malformed/count-exceeding replies and prevent unsafe replay | Executable wire-boundary and provider-reply tests; defensive adaptation, not native malformed-buffer behavior |
| `NS_IO_010` | `sysfile.c:sseek`, `sys/man/2/seek` | Absolute, atomic relative, and end-relative regular-file seek; provider-stream directory seek defers device/member/overflow reset until read at zero | Native directory rewind tested locally; stream/pipe capability discrimination remains pending. Arithmetic overflow is rejected explicitly rather than emulating C overflow |
| `NS_IO_012`, `NS_IO_013` | `sysfile.c:fdtochan`, `fdclose`; `proc.c:pexit` ownership extended to async completion | `VProcess` atomically admits descriptor leases and publishes opens under its lifecycle lock; provider waits retain the original channel | Covered locally by exit-with-surviving-child BDD, pending-read close/reuse/exit tests, and cleanup tests; durable Orleans fencing remains pending |
| `NS_IO_014` | Fog async completion extension | Provider-acknowledged cancellation releases the retained lease; no automatic replay is performed | Partial test evidence only; lost-reply reconciliation, durable operation ownership, and termination cancellation remain pending |
| `NS_IO_015`, `NS_IO_016` | `chan.c:Acreate`, `sysfile.c:syscreate`; async process publication extension | Definite rejection fallback, uncertain failure suppression, and last-owner exit during provider create | Executable coverage in `Plan9CreateTests`; dedicated BDD bindings for these two additional design scenarios remain pending |
| `NS_META_001`–`NS_META_015`, `NS_META_038`, `NS_META_040` | `sysfile.c:sysstat`, `sysfstat`, `syswstat`, `fdtochan`, `pathlast`, `dirsetname`; `chan.c:namec`; `devmnt.c:mntstat`; `sys/man/2/stat` | Process metadata paths, saved visible names, retained open-handle dispatch, bounded raw replies, actual rename and zero-element final mounts implemented with eight BDD scenarios plus unit/property/fuzz and real-grain evidence | Local syscall behavior implemented; durable remote-fid recovery remains separate; see `MetadataSyscalls.md` |
| `NS_META_016`–`NS_META_024` | `sysfile.c:validstat`, `syswstat`, `sysfwstat`, `wstat`; `convM2D.c:statcheck`; `chan.c:validname`; `devmnt.c:mntwstat` | Exact structural/name validation, sentinel forwarding, retained mount-point/CMSG state, device results and descriptor preservation implemented | Executable BDD/unit/property/fuzz coverage; checked prefix follows source over the conflicting manual sentence |
| `NS_META_025`–`NS_META_031`, `NS_META_039` | `sys/man/5/stat`; `sys/man/2/stat`; raw encoding and managed boundary policy in `MetadataSyscalls.md` | Raw size limits, protected-field rejection, atomic combined updates, collisions, directory length and malformed replies tested in memory and real-grain providers | Authorization/group-leadership policy remains each production provider's responsibility; optional barrier and outer Twstat transport limits are capability boundaries |
| `NS_META_032`–`NS_META_037` | Retained `fdtochan`/`namec` references adapted to asynchronous hosts; `DistributedLifetimes.feature`; `WStatRecovery.feature` | Stat/fstat/wstat/fwstat admission, immutable request ownership, close/reuse/exit/cancellation and namespace-change races plus durable lost-reply reconciliation covered by local and real-grain tests | Wstat/fwstat recovery persists the selected resource or retained handle and original operation identity; pathname remove recovery remains separate |
| `NS_REMOVE_001`–`NS_REMOVE_007`, `NS_REMOVE_014` | `sysfile.c:sysremove`; `chan.c:namec(Aremove)`; `devmnt.c:mntremove`; `sys/man/2/remove`, `sys/man/5/remove` | Fid/data-plane remove exists; dedicated pathname syscall with private lookup ownership, mount-point refusal and no double clunk is specified | Native design; pending implementation and executable bindings |
| `NS_REMOVE_008`–`NS_REMOVE_009` | `sys/man/2/remove`, `sys/man/5/remove` | Parent permissions, empty directories and provider-dependent other-fid behavior are specified | Provider contract; pending conformance tests |
| `NS_REMOVE_010`–`NS_REMOVE_013` | Native channel ownership adapted to async completion; `DistributedLifetimes.feature` | Namespace/exit/cancellation races and lost-reply removal of replacement-file prevention are specified | Async extension; pending implementation and durable recovery evidence |
| `WASI_NS_001`–`WASI_NS_014` | Preview 1 WITX and Fog's `fog-v1-profiles/Wasm.md` | dotnet-webassembly primary workload: capabilities, marshalling, renumber, provider extensions and worker isolation | Adapter extension; ten executable engine compatibility cases provide narrower evidence only |

The largest semantic gaps before claiming 9front namespace compatibility are exact
`Chan`/`Path` identity and history, distributed namespace lifecycle, native `/proc` namespace
reconstruction, and the `/srv` and `/shr` providers. The control
filesystem and CSP features should remain tagged as extensions because they are
distributed interfaces designed around, rather than defined by, Plan 9.

The lifecycle design gap has a concrete contract in
[ResourceLifetimes.md](ResourceLifetimes.md). This does not change the implementation
status of distributed `NS_PROC_005`: local descriptor ownership is now implemented;
durable Orleans cleanup still requires implementation and the prescribed recovery gates.

Local cleanup is also an explicit executable feature: `DescriptorCleanup.feature`
exercises `NS_FD_004`, `NS_FD_005`, `NS_FD_014`, and `NS_FD_015`, including pending
provider completion, close failure, rejection of new ownership after detachment,
descriptor-order release, and repeated termination. These scenarios complement
the fid cleanup scenarios and the pending distributed `NS_LIFE_*` feature set.

The file syscall contract is [ProcessFileSyscalls.feature](ProcessFileSyscalls.feature).
Checked-in 9front `sseek` permits directory seek to absolute zero and rejects pipe
seek with `Eisstream`; `sys/man/2/seek` instead disallows directory seeking and calls
pipe seeking a no-op. These scenarios explicitly follow the source behavior.
Native overlapping writes reserve requested ranges before device IO and correct
position after short/failing writes. They do not serialize all native IO; any stricter
WASI adapter policy is separate. Directory capability confinement, rights and renumber
also belong to the adapter. WASI CREATE without TRUNC is not native Plan 9 create.

Native create uses the existing byte-mode provider operation: create(5) itself must
reject an existing name atomically, so OEXCL does not need a new wire bit. Only the
syscall request retains OEXCL; both OEXCL and descriptor-local OCEXEC are absent from
the provider mode. A successful create returns the created channel directly, as in
`chan.c:Acreate`, without re-crossing mounts. This also permits late completion to
reach descriptor-publication cleanup after the namespace's final owner exits.

Fog distinguishes an acknowledged provider rejection (`ResourceCreateRejectedException`)
from an uncertain IO/transport failure. Orleans providers use the serializable
`ResourceCreateRejectedGrainException`, which the adapter translates. Native fallback
is allowed for these definite rejections and local MCREATE-selection denial; generic
IO errors, cancellation and lost replies do not trigger a truncating retry. This is
an explicit distributed completion distinction; durable reconciliation is still pending.

The local descriptor table additionally supplies atomic `RenumberAsync` for a future
WASI adapter. It moves ownership and OCEXEC flags and releases displaced ownership
outside the table lock. It is an extension, not a native Plan 9 syscall or a complete
WASI `fd_renumber` implementation (the table retains native growth bounds).

`Plan9FileSyscalls` is a host API bound to a `VProcess` and its matching data plane.
It is not yet exported as a new 9P ctl command or wired into a WASI execution host.
