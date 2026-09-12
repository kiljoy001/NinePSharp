# Plan 9 namespace compatibility

These Gherkin specifications define the behavioral target for a distributed Plan 9 namespace implemented by NinePSharp and Orleans.

`NamespaceCore.feature` and the namespace portions of `ProcessNamespaces.feature`
are compatibility scenarios derived from 9front source and manuals. The control
filesystem, distributed service projections, and CSP resources are explicitly
NinePSharp extensions that preserve Plan 9's resource model while adding remote
and asynchronous behavior.

The scenarios are divided into four layers:

- `NamespaceCore.feature` covers channel identity, mount heads, unions, traversal, and mutation.
- `ProcessNamespaces.feature` covers process groups and the `bind`, `mount`, `unmount`, `chdir`, and `rfork` surface.
- `NamespaceSyscalls.feature` covers the detailed `bind(2)`/`mount(2)`/`unmount(2)` contract.
- `NamespaceControlPlane.feature` covers the 9P filesystem used to request remote namespace operations.
- `NamespaceDevices.feature` covers `/proc`, `/srv`, and `/shr` projections.
- `AsyncResourceChannels.feature` covers CSP-style message resources exposed through the namespace.

The scenarios describe observable behavior. They do not require the implementation to copy 9front's internal data structures, provided that channel identity, path traversal, ordering, permissions, and lifecycle behavior remain compatible.

See [Traceability.md](Traceability.md) for the source/manual mapping and current implementation status.
