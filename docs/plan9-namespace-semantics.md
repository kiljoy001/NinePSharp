# Plan 9 Namespace Semantics Used by NinePSharp

This package follows the namespace behavior in the adjacent 9front source tree rather
than treating a namespace as a dictionary from textual paths to services.

## Source Map

| Behavior | 9front source |
| --- | --- |
| `Chan`, `Mount`, `Mhead`, and `Pgrp` layouts | `sys/src/9/port/portdat.h` |
| replacement and ordered union construction | `sys/src/9/port/chan.c:cmount` |
| unmounting all or one union member | `sys/src/9/port/chan.c:cunmount` |
| mount lookup by `(type, dev, qid.path)` | `sys/src/9/port/chan.c:findmount` |
| mount crossing, union fallback, and `..` | `sys/src/9/port/chan.c:walk` |
| union directory reads | `sys/src/9/port/sysfile.c:unionread` |
| create selection using `MCREATE` | `sys/src/9/port/chan.c:createdir` |
| namespace sharing and `RFNAMEG` copying | `sys/src/9/port/sysproc.c`, `pgrp.c:pgrpcpy` |
| global shared 9P mountpoints | `sys/src/9/port/devshr.c`, `sys/man/3/shr` |

## Deliberate Model

`ResourceIdentity` is the equivalent of the stable parts of a channel identity. Qid
version is retained by `ResourceHandle` for cache coherence but is not part of mount
lookup. `MountTable` is the equivalent of the mount hash owned by a `Pgrp`.

`NamespaceChannel` retains object handles and visible traversal frames. It never
re-resolves an existing fid from a textual path, so a mount crossing can be reversed by
walking `..` and a rename does not inherently invalidate the channel.

When a channel is positioned at a mount point, navigation consults the current mount
head. An already-open union channel therefore observes later `MBEFORE`, `MAFTER`, and
unmount operations, like a 9front channel retaining an `Mhead`. Binding such a channel
copies its complete ordered union, including the `cmount` restriction on `MCREATE`.

`VProcessGroup` owns one `MountTable`. A child can share that object, receive an
independent ordered snapshot, or receive an empty namespace. These correspond to the
ordinary shared namespace group, `RFNAMEG`, and `RFCNAMEG` behavior.

## Orleans Distribution Boundary

`IVProcessGroupGrain` is the durable, serialized owner of a mount table.
`IVProcessGrain` stores a process's root and current-directory channels plus the key of
that group. Forking with `Share` reuses the group key, `Copy` initializes a new group
from `MountTable.Clone()`, and `Empty` initializes a blank group.

Mountable application grains implement `IMountableResourceGrain`. Their logical grain
key is the `Device` portion of `ResourceIdentity`; the `Provider` portion lets an
application resolver choose the correct grain implementation. `OrleansResourceOperations`
routes walks, directory reads, and creates directly to those grains.

`DistributedNamespaceOperations` fetches one process-group snapshot for an operation,
applies mount crossing and union rules locally, and then calls resource grains. The
process-group grain is therefore on the namespace-control path but not on the file-data
or compute-data path.

## Current Limits

- Directory entries are concatenated in union order. Duplicate names are not removed,
  matching `unionread`.
- `MCACHE` is represented but no cache policy is imposed by the namespace layer.
- A distributed data operation sees a consistent mount snapshot fetched at its start;
  it does not restart automatically if the group changes while a remote grain call is
  in flight.
- Resource grains currently expose namespace traversal, directory reads, and creation.
  Open file I/O and full 9P stat mutation remain separate backend responsibilities.
- Nested mount-on-mount chains at one visible path and 9front's `mchan` alias used by
  selected unmount are not yet represented.

These limits are explicit so later Orleans persistence and routing do not accidentally
claim stronger Plan 9 compatibility than the implementation provides.
