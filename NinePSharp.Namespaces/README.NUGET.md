# NinePSharp.Namespaces

Plan 9-style namespace composition for NinePSharp. The package models mount tables,
ordered union directories, channel traversal, and virtual process namespace sharing
without coupling the namespace kernel to a storage or actor runtime.

Virtual processes also own independent descriptor groups. `Fork` accepts a
`DescriptorForkMode` alongside the namespace mode (default descriptor sharing is
rfork behavior). Copies preserve descriptor flags and share open channels.
`RforkDescriptorsAsync` changes the current process's descriptor membership.

Install a newly opened `ResourceOpenHandle` with a final-close callback through
`process.Descriptors.Install`. Ownership transfers only on success; close the
provider handle yourself if installation fails. `DuplicateAsync` shares an
existing open instance, and `Acquire` returns an async-disposable lease for I/O.
The provider must remain usable until all slots and leases release it.

`TerminateAsync` releases process ownership and awaits resulting local cleanup.
The synchronous `Terminate` detaches the process and exposes pending cleanup via
`VProcess.TerminationCompletion`. Native close semantics swallow provider-close
errors; this local layer does not provide a durable retry ledger. Distributed
descriptor recovery is not included yet.

`Plan9FileSyscalls` supplies native path open/create, regular-file read/write, positioned
read/write (including the native -1 sentinel), and seek over a virtual process.
Duplicated and copied descriptors share positions. Async operations retain channel
leases, and an open finishing after process exit closes its provider handle instead
of publishing into a surviving shared table. Hosts must supply the matching process
data plane and unique authenticated operation contexts. Provider permissions,
OTRUNC, and ORCLOSE remain provider responsibilities.

`CreateAsync(path, new Plan9CreateRequest(permissions, mode))` preserves the native
OEXCL bit. Existing names are opened with OTRUNC unless OEXCL requests failure;
absent names use atomic provider create(5). Providers must reject collisions without
truncation. A `ResourceCreateRejectedException` acknowledges a definite rejection
and permits ordinary create to retry its lookup; generic transport errors do not.

By default, directory `ReadAsync` uses a shared metadata-backed cursor to return complete 9P2000
stat records within the requested byte count. Duplicates and copied tables share
that cursor. Directory `PReadAsync` accepts zero (refresh), the current offset, or
the implicit -1 sentinel, and advances the shared cursor. `SeekAsync(fd, 0, Set)`
resets it. Listings are cached until rewind; an implicit read still at offset zero
refreshes an empty listing. Buffers too small for the next record fail without
consuming it. Cursor reads and rewind serialize asynchronously per open channel.

This metadata adapter preserves visible union order and duplicate names. It does
not yet represent separate raw-provider offsets, native per-member open handles,
mountfix overflow buffers, or live union changes during an enumeration. The existing
9P fid gateway remains a separate interface with its existing directory behavior.

Hosts with byte-stream directory providers can select native streaming explicitly:

```csharp
var directoryStats = new DirectoryStatOperations(resourceOperations,
    new[] { new DirectoryDeviceBinding(7, 0, "my-provider", "my-device") });
var files = new Plan9FileSyscalls(process, dataPlane, nextOperationContext,
    DirectoryReadMode.ProviderStream, directoryStats);
```

The provider's raw stat records must use the registered type/device identities.
Streaming reads use retained open handles and byte offsets, lazily open union
members, reflect live member changes, rewrite mounted metadata and retain mountfix
overflow. Seek to zero defers provider/union reset until the next read at zero.
Native overflow order and counter accounting follow the pinned 9front source.

Use `MountTable.MountAsync` and `UnmountAsync` when streams can be active. Direct
synchronous mutations fail busy instead of blocking on provider IO. Namespace
syscalls already use the asynchronous paths. The current per-head gate serializes
union steps on that head; unrelated heads remain independent.

Providers report definite directory rejections with `ResourceDirectoryRejectedException`;
the Orleans adapter translates `ResourceDirectoryRejectedGrainException`. Member
open is an atomic clone/open provider abstraction: a rejected open must clean up
its own internal clone. Caller cancellation can cancel waiting for the cursor but
does not abandon admitted provider IO. Unknown outcomes prevent replay, and an
unknown member-open error retains its original operation context for the host.
Durable reconciliation of unreturned handles remains future work.

Process metadata uses an explicit bounded stat provider:

```csharp
var stats = new FileStatOperations(resourceOperations,
    new[] { new DirectoryDeviceBinding(7, 0, "my-provider", "my-device") });
var journal = new MemoryWStatRecoveryStore(); // Use OrleansWStatRecoveryStore in an Orleans host.
var durableStats = new DurableFileStatOperations(stats, journal);
var files = new Plan9FileSyscalls(process, dataPlane, nextOperationContext,
    fileStats: durableStats);
ReadOnlyMemory<byte> metadata = await files.StatAsync("/data/report", 4096);
ReadOnlyMemory<byte> openedMetadata = await files.FStatAsync(fd, 4096);
byte[] update = FileStatOperations.EncodeUpdate(ResourceWStat.Unchanged() with
{
    Mode = NinePConstants.Mode0600,
});
uint pathResult = await files.WStatAsync("/data/report", update);
uint descriptorResult = await files.FWStatAsync(fd, update);
```

These calls return one raw stat record or a two-byte required-payload-size hint.
Add two to the unsigned little-endian hint for a retry capacity; a longer visible
name can require another retry. The syscall does not retry internally. Fstat uses
the retained provider handle and saved visible name, preserving offsets and
descriptor ownership across namespace changes. Providers must implement
`IResourceOpenStatOperations`; mutation providers implement
`IResourceWStatOperations`. Orleans providers register `IOpenStatResourceGrain`
or `IWStatResourceGrain` for the corresponding capabilities. Unsupported providers fail explicitly. A custom
`IFileStatOperations` adapter owns any temporary protocol fids inside its operation.
Once dispatched, stat completion and cleanup continue even if the caller cancels.
If `DurableFileStatOperations` reports `WStatRecoveryPendingException`, retain its
operation identity and call `RecoverAsync` instead of issuing a new mutation. The
provider must persist idempotency results for the same identity. Pathname `remove`
and its lost-reply recovery remain follow-up work.
