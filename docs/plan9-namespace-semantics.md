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
ordinary shared namespace group, `RFNAMEG`, and `RFCNAMEG` behavior. The table also
persists a namespace-wide mount-disabled state and blocked device names, which model
`RFNOMNT` and the device mask checked by `canmount`.

## Orleans Distribution Boundary

`IVProcessGroupGrain` is the durable, serialized owner of a mount table.
`IVProcessGrain` stores a process's root and current-directory channels plus the key of
that group. Forking with `Share` reuses the group key, `Copy` initializes a new group
from `MountTable.Clone()`, and `Empty` initializes a blank group. `RforkNamespaceAsync`
applies the same share, copy, or empty choices to the current process without creating
a child.

Mountable application grains implement `IMountableResourceGrain`. Their logical grain
key is the `Device` portion of `ResourceIdentity`; the `Provider` portion lets an
application resolver choose the correct grain implementation. `OrleansResourceOperations`
routes walks, directory reads, and creates directly to those grains.

`DistributedNamespaceOperations` fetches one process-group snapshot for an operation,
applies mount crossing and union rules locally, and then calls resource grains. The
process-group grain is therefore on the namespace-control path but not on the file-data
or compute-data path.

`DistributedNamespaceDataPlane` routes open, read, write, stat, create, clunk, and
remove operations to the same resource grains. Each request carries an operation ID;
the durable resource contract uses it to make retries idempotent across grain
activation and migration.

## Fid Sessions and Wire Protocol

`NamespaceSession` owns the fid table for one 9P connection. It serializes operations
on each fid, keeps walked channels independent, preserves successful prefixes for
partial walks, updates the fid after create, and releases all open resource handles on
disconnect. Clunk and remove invalidate their fid before provider cleanup, including
when that cleanup fails, matching the fail-stop ownership rule used by 9front.

`NamespaceSyscalls` provides the path-based bind, mount, and unmount layer above the
object-based mount table. It resolves bind sources at call time, leaves final mount
targets untranslated, validates service descriptor mode and authentication, carries
the server attach name, and closes a successfully mounted source descriptor.

`NamespaceControlResource` projects a process table as a 9P resource tree. An attached
control root exposes `/proc/<pid>/ns` and `/proc/<pid>/status` for inspection and a
writable `/proc/<pid>/ctl` accepting `bind`, `mounts-disabled`, `unmount`, and `rfork`
commands. It implements `IResourceDataOperations`, so hosts can attach it to the local
data plane or register an equivalent provider for the Orleans gateway.

`NinePSharp.Namespaces.Orleans.Server` connects that session model to the existing
stream server. It implements the classic attach, walk, open, read, write, stat, create,
clunk, remove, and flush request path. Flush can cancel a request already in flight;
the stream processor therefore permits requests after version negotiation to execute
concurrently while the fid session provides the required per-fid ordering.

The dispatcher also negotiates 9P2000.L and supports `Tlopen`, `Tlcreate`, `Treaddir`,
and `Tgetattr`. Shared messages are parsed alongside Linux extensions; attach includes
the numeric-user field for the Unix/Linux dialects.
Other Linux-extension requests return `Rlerror` with `EOPNOTSUPP`; this is a maintained
9P2000.L subset, not a claim of complete Linux dialect support.

## Current Limits

- Directory entries are concatenated in union order. Duplicate names are not removed,
  matching `unionread`.
- `MCACHE` is represented but no cache policy is imposed by the namespace layer.
- Mount policy is represented as a namespace-wide restriction plus blocked device
  names; it does not yet reproduce 9front's fixed device-number mask.
- Providers own wire Qid allocation. `ResourceIdentity.Path` is emitted directly,
  so devices combined into one export must use non-colliding stable Qid paths.
- A distributed data operation sees a consistent mount snapshot fetched at its start;
  it does not restart automatically if the group changes while a remote grain call is
  in flight.
- Resource grains expose traversal, directory reads, creation, open file I/O, stat,
  clunk, and remove. Wstat/setattr and the remaining 9P2000.L operations remain backend
  and protocol work.
- Nested mount-on-mount chains at one visible path and 9front's `mchan` alias used by
  selected unmount are not yet represented.

See [hosting the gateway](orleans-integration.md) for registration, security, delivery
semantics, and the runnable example.

These limits are explicit so Orleans persistence and routing do not accidentally
claim stronger Plan 9 compatibility than the implementation provides.
