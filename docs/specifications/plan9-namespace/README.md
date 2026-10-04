# Plan 9 namespace compatibility

These Gherkin specifications define the behavioral target for a distributed Plan 9 namespace implemented by NinePSharp and Orleans.

`NamespaceCore.feature` and the namespace portions of `ProcessNamespaces.feature`
are compatibility scenarios derived from 9front source and manuals. The control
filesystem, distributed service projections, and CSP resources are explicitly
NinePSharp extensions that preserve Plan 9's resource model while adding remote
and asynchronous behavior.

The specifications are organized by behavior:

- `NamespaceCore.feature` covers channel identity, mount heads, unions, traversal, and mutation.
- `ProcessNamespaces.feature` covers process groups and the `bind`, `mount`, `unmount`, `chdir`, and `rfork` surface.
- `NamespaceSyscalls.feature` covers the detailed `bind(2)`/`mount(2)`/`unmount(2)` contract.
- [ProcessFileSyscalls.feature](ProcessFileSyscalls.feature) specifies file syscalls,
  including async exit races and cleanup. The first regular-file slice has executable
  coverage, including native create/OEXCL and metadata-backed directory cursors;
  provider-stream directory reads are available as an explicit host mode;
  distributed completion recovery remains pending.
- [DirectoryStreaming.feature](DirectoryStreaming.feature) expands native directory
  reads into provider streams, live union traversal, mountfix overflow, rewind and
  cleanup scenarios. [DirectoryStreaming.md](DirectoryStreaming.md) records the
  pinned source comparison, quirks, async adaptations and implementation evidence.
  Five executable BDD cases and targeted regression/property tests cover streaming;
  the full design file is not automatically bound wholesale.
- [MetadataSyscalls.feature](MetadataSyscalls.feature) specifies `stat`, `fstat`,
  `wstat` and `fwstat`; [RemoveSyscall.feature](RemoveSyscall.feature) specifies
  pathname removal, temporary fid consumption and provider-dependent open-handle
  behavior. [MetadataSyscalls.md](MetadataSyscalls.md) maps the source/manual
  contract, discrepancies, cleanup and verification requirements. These are design
  specifications with stat/fstat/wstat/fwstat implementation and eight executable
  BDD scenarios; wstat/fwstat lost-reply recovery is implemented, while pathname
  removal and its durable recovery remain pending.
- Fog's WASI namespace adaptation (`docs/specifications/fog-v1-profiles/Wasm.md` in the Fog
  repository) makes dotnet-webassembly its primary workload target without changing native
  Plan 9 syscall semantics.
- `NamespaceControlPlane.feature` covers the 9P filesystem used to request remote namespace operations.
- `NamespaceDevices.feature` covers `/proc`, `/srv`, and `/shr` projections.
- `AsyncResourceChannels.feature` covers CSP-style message resources exposed through the namespace.
- [ResourceLifetimes.md](ResourceLifetimes.md) defines process, namespace-group,
  descriptor-group, channel, and fid ownership, including the Orleans recovery protocol.
- `DescriptorLifetimes.feature` specifies native descriptor inheritance, duplication,
  close, exit, and final channel release. Local ownership has executable coverage;
  regular-file syscall admission and shared offsets also have executable coverage.
- `DistributedLifetimes.feature` specifies durable ownership, fencing, and recovery;
  executable bindings are pending. These are distributed extensions.

The executable fid scenarios live in
[`FidSessions.feature`](../../../NinePSharp.Namespaces.Tests/Features/FidSessions.feature)
and [`FidUnmount.feature`](../../../NinePSharp.Namespaces.Tests/Features/FidUnmount.feature).
The latter covers retained open/unopened file fids across selected/complete
unmount and replacement, fresh path lookup, and final clunk/disconnect cleanup.

The executable descriptor feature set includes
[`DescriptorLifetimes.feature`](../../../NinePSharp.Namespaces.Tests/Features/DescriptorLifetimes.feature)
for inheritance, final-owner exit, and admitted I/O references, and
[`DescriptorCleanup.feature`](../../../NinePSharp.Namespaces.Tests/Features/DescriptorCleanup.feature)
for pending/failed provider close, refusal of new ownership during cleanup,
empty-table rfork, close-on-exec, and repeated termination. The corresponding
distributed cleanup acceptance cases are `NS_LIFE_008` through `NS_LIFE_013` and
`NS_LIFE_018`; they remain pending implementation.

[`ProcessFileSyscalls.feature`](../../../NinePSharp.Namespaces.Tests/Features/ProcessFileSyscalls.feature)
executes shared-position, explicit-position, failed-write, and late-open cleanup
scenarios. `Plan9FileSyscallsTests` adds access modes, namespace lookup, independent
opens, copied descriptors, overlapping writes, seek bounds, close/reuse, cancellation
acknowledgement, and allocation failure. The `namespace-syscalls` fuzz target now checks
generated regular-file transfers against an independent offset model.

Native create coverage adds existing-file truncation without metadata replacement,
exclusive-create collisions, overlapping create attempts, MCREATE selection, full
descriptor tables, and last-owner exit while the provider is completing creation.
The Orleans suite exercises definite rejection serialization through real test silos;
the namespace fuzz target checks generated ordinary/exclusive creation sequences.

[`DirectoryCursors.feature`](../../../NinePSharp.Namespaces.Tests/Features/DirectoryCursors.feature)
executes the metadata-backed directory cursor profile: complete bounded stat records,
dup sharing, rewind refresh, ordered union duplicates, and undersized-buffer errors.
The tests also cover copied tables, independent opens, UTF-8 metadata, directory
pread offsets, cancellation, and retained handles during pending reads. This profile
adapts the existing whole-directory metadata API; it does not complete native
`unionread`/`mountfix` or introduce byte-stream directory providers.
See [DirectoryCursors.md](DirectoryCursors.md) for the adapter contract and explicit
differences from native directory streaming.

The opt-in provider-stream mode adds retained raw handles, separate offsets, lazy
union enumeration, live mount-head behavior and mountfix overflow. See
[DirectoryStreaming.md](DirectoryStreaming.md#implementation-evidence-2026-09-16)
for configuration requirements, scenario evidence and remaining durable-recovery work.

The scenarios describe observable behavior. They do not require the implementation to copy 9front's internal data structures, provided that channel identity, path traversal, ordering, permissions, and lifecycle behavior remain compatible.

See [Traceability.md](Traceability.md) for the source/manual mapping and current implementation status.
See [FileSyscallValidation.md](FileSyscallValidation.md) for the first regular-file
slice's source revision, test commands, and validation results.
