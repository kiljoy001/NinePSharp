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
| `NS_PROC_005` | `pgrp.c:closepgrp` | No public close/termination operation currently exists | Missing lifecycle binding |
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
| `NS_CTL_001`–`NS_CTL_008` | No native 9P equivalent; native `bind`/`mount`/`unmount` are syscalls in `sys/man/2/bind` | No namespace control tree currently exists | NinePSharp distributed extension |
| `NS_DEV_001` | `devproc.c:readns1` | No `/proc/<pid>/ns` provider currently exists | Missing |
| `NS_DEV_002` | `devsrv.c`, `sys/man/3/srv` | Current Orleans resource registration is not a `/srv` descriptor registry | Distributed projection extension |
| `NS_DEV_003`–`NS_DEV_004` | `devshr.c`, `sys/man/3/shr` | No global shared mount registry currently exists | Missing |
| `NS_DEV_005` | `sys/man/4/namespace`, ordinary 9P provider behavior | Distributed resource grains already participate in ordinary walk/read operations | Covered for mounted providers |
| `NS_ASYNC_001`–`NS_ASYNC_008` | No CSP primitive in Plan 9 namespace semantics | No message-channel grain/resource currently exists | NinePSharp application extension |

The largest semantic gaps before claiming 9front namespace compatibility are exact
`Chan`/`Path` identity and history, namespace lifecycle, and the `/proc`, `/srv`,
and `/shr` providers. The control
filesystem and CSP features should remain tagged as extensions because they are
distributed interfaces designed around, rather than defined by, Plan 9.
