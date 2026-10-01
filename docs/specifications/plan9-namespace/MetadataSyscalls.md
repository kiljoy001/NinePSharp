# Metadata and removal syscall contract

[MetadataSyscalls.feature](MetadataSyscalls.feature) specifies `stat`, `fstat`,
`wstat` and `fwstat`. [RemoveSyscall.feature](RemoveSyscall.feature) specifies
pathname `remove`. Stat/fstat/wstat/fwstat now have local implementations and
executable evidence, detailed below. Pathname remove and durable distributed
recovery remain design specifications pending implementation and bindings.
Existing fid/provider methods alone do not establish process syscall conformance.

The two feature files contain 54 uniquely tagged scenarios/outlines, expanding to
97 cases. Gherkin 29.0.0 parsing, example-column/placeholder checks, category/ID
checks and whitespace validation passed on 2026-09-16. Existing namespace design
features also parse. This is specification validation, not runtime test evidence.

## Reference and precedence

Compared on 2026-09-16 against the sibling `../9front` checkout at
`9654fe7fa882f8043c267bbeb6679ebd9102209b`. The consulted files have no local
modifications. Native cases follow this pinned implementation when a manual
disagrees or leaves behavior unspecified.

| Source or manual | Contract |
| --- | --- |
| `sys/man/2/stat` | Path versus descriptor calls, status fields, unchanged-field sentinels, result counts and short-buffer hints |
| `sys/man/5/stat` | Record encoding, provider permissions, atomic updates, forbidden changes, optional durability barrier and outer 9P length limit |
| `sys/man/2/remove`, `sys/man/5/remove` | Parent-directory permissions, empty directories, consuming the remove fid on error, provider-dependent behavior of other fids |
| `sys/src/9/port/sysfile.c:sysstat`, `sysfstat`, `pathlast`, `dirsetname` | Provider dispatch, retained-path name rewriting, result counts and reference release |
| `sysfile.c:syswstat`, `sysfwstat`, `validstat`, `wstat` | Validation ordering, small-name validation, descriptor admission, mount-point rename restriction and device result |
| `sysfile.c:fdtochan`, `fdclose`; `chan.c:cclose` | Any-open-mode descriptor lookup, CMSG distinction and retained channel ownership |
| `sysfile.c:sysremove` | Aremove lookup, mount-point refusal, remove-consumes-fid handling on success and error |
| `sys/src/9/port/chan.c:namec`, `walk`, `domount`, `cunique` | Aaccess/Aremove private channels, ordered lookup, final mount crossing, retained path and `ismtpt` marker |
| `chan.c:validname0`, `isfrog` | Name validation used by `validstat` |
| `sys/src/9/port/devmnt.c:mntstat`, `mntdirfix`, `mntwstat`, `mntremove`, `mntclunk` | Mounted 9P short-buffer behavior, local device identity, request sizes and remove dispatch |
| `sys/src/9/port/dev.c:devstat`; `sys/src/libc/9sys/convD2M.c` | Local-device encoding and provider-specific errors below a two-byte capacity |
| `sys/src/libc/9sys/convM2D.c:statcheck` | Exact raw record length and four string-boundary checks |
| `sys/src/libc/9sys/nulldir.c`, `dirfwstat.c`, `dirfstat.c` | Sentinel initialization, structured convenience encoding and libc's bounded retry helper |

`@native` identifies observable syscall behavior. `@provider_contract` identifies
the file-server protocol or a managed adapter's defensive boundary; it does not
move all permission checks into the namespace kernel. `@async_adaptation`
identifies Fog admission, ownership and recovery rules with no claim that native
9front implements Orleans cancellation or durable journals.

## Lookup, channel state and descriptor admission

Path `stat` and `wstat` use `Aaccess`; `remove` uses `Aremove`. Both modes follow
normal namespace lookup and final `domount`, obtain a private channel via
`cunique`, restore its visible path and record whether the final resource was a
mount point. They do not open the file for content IO or allocate an fd. This
applies even to `.` or `/`, where there may be no final component walk. Lookup
still enforces the normal trailing-slash/directory and path traversal rules.

`fstat` uses `fdtochan(fd, -1, 0, 1)`; `fwstat` uses
`fdtochan(fd, -1, 1, 1)`. Both acquire a reference without requiring a content
read/write mode. Only the latter rejects a `CMSG` channel. **CMSG denotes the
mount transport**, not every file reached through a mount. The retained channel
and its provider handle determine descriptor metadata; pathname lookup must not
be repeated to reconstruct them.

The channel must retain enough state to distinguish:

- Provider/open-handle identity from the displayed path name.
- Its saved path from the caller's current namespace.
- Its saved `ismtpt` marker from a fresh search of the current mount table.
- A transport marked CMSG from an ordinary mounted file.

Mounting over a path after an ordinary open does not mark the old channel.
Unmounting after a mount-point open does not clear its marker. Renaming a file
through `wstat` does not update the kernel channel's stored path. None of these
operations changes the channel's sequential offset or rewinds a directory cursor.
Provider metadata changes can affect later IO, but the syscall does not refresh
buffered directory listings or clamp an offset when a file is truncated.

An ordered union chooses a target during lookup. Stat of the union root uses its
first mounted resource. A later stat/update/remove failure on the selected target
does not repeat the operation on other members. `MCREATE` does not select a
metadata or removal target.

## Stat records, names and bounded replies

The raw syscall returns a byte count and either one complete stat record or a
two-byte size hint. It does not return an Rstat envelope. The record's little-endian
unsigned prefix counts the bytes **after** that prefix. Complete records contain
the fixed 49-byte minimum and four counted UTF-8 strings, with byte lengths rather
than character lengths. Type/device identity must come from an explicit provider
mapping, not accidental truncation or hashing of a provider name. The native mount
driver fills its local device identity through `mntdirfix`.

`sysstat` and `sysfstat` call the device once and then replace the returned name
using `pathlast(c->path)` when a path exists. Other metadata and strings are
preserved. Source-specific details:

- Path `/alias/report` displays `report`, even if the provider names it differently.
- A retained path string `/` produces an **empty** name: `pathlast` returns the
  characters after the final slash. This differs from the server-root `/` name
  required by `stat(5)` at the provider boundary. An absent or zero-length path
  bypasses rewriting.
- A two-byte provider response is returned unchanged. The visible name cannot yet
  be substituted, so its size difference is not included in that hint.
- For a complete provider response, `dirsetname` recalculates total size. If the
  renamed record would exceed capacity, it returns two bytes with the revised
  payload size; otherwise it shifts the remaining strings and returns the complete
  rewritten record.
- Consequently a provider size of 80 bytes and a visible name 12 bytes longer can
  require caller capacities 2, then 80, then 92. A shorter name cannot help until
  the provider's complete record fits. The syscall itself does not retry.
- `mntstat` rejects capacities below two before sending Tstat. Local `devstat`
  also fails such capacities, but its error text differs. The feature's exact
  short-stat error is scoped to the mounted 9P driver, not every possible device.

The libc `dirfstat` helper tries at most twice; it is not a promise of unlimited
retries or a stable snapshot. Structured conveniences added later must document
their retry policy separately from this raw syscall contract.

A raw unsigned prefix can describe 65535 payload bytes, or 65537 bytes including
the prefix. By contrast Rstat/Twstat have an **outer** unsigned 16-bit count that
includes the complete inner record and therefore limits it to 65535 bytes, also
subject to negotiated transport limits. A transport adapter must reject an
unrepresentable message. Managed name rewriting must likewise reject a result
whose inner prefix cannot represent its new size, rather than copying native
`PBIT16` truncation. These overflow safeguards are adapter policy, not a claim
that every native device checks them.

## Wstat validation and atomic updates

Both syscalls run `validstat` before target lookup or fd acquisition. A malformed
record therefore wins over an invalid target. The pinned `statcheck` requires:

1. At least `STATFIXLEN` (49) bytes.
2. A leading size prefix equal to supplied length minus two.
3. Four bounded string counts and exactly the end of the supplied buffer.

This contradicts the sentence in `stat(2)` saying that the leading size field is
ignored. The specification deliberately follows **the checked-in source**.
It also prevents reusing a decoder that accepts trailing bytes or ignores the
record's declared length.

`validstat` performs a limited additional name check. Names of at most 63 bytes
are copied into its 64-byte scratch buffer and passed to `validname`, except `/`
is explicitly accepted. Empty names and `.`/`..` are not rejected by this kernel
helper; the provider decides whether they are meaningful changes. Names of 64
bytes or more bypass the helper's name check and go to the provider. Short names
containing a slash, nonzero ASCII control bytes or DEL are rejected. The native
helper uses C strings, so embedded NUL terminates its inspection; it is not a
complete hostile-UTF-8 validator. The native scenarios assume conforming string
data except where they explicitly test a source validation rule. A stricter
managed string policy must be identified as an adaptation, not attributed to this
kernel helper.

On a channel marked `ismtpt`, any **nonempty** name is rejected, even if it equals
the current displayed name. Empty-name updates may reach the mounted target's
provider. This protection also applies to an fd whose mount-point marker was
retained before unmount.

The request must preserve field widths and unchanged-field sentinels: all ones
for metadata integers and empty strings for unchanged text. The size prefix and
string counts remain valid framing, not sentinels. A nullable patch model is
acceptable only if it serializes these distinctions exactly. A full `ResourceStat`
snapshot plus read-modify-write is not a substitute: it can overwrite concurrent
provider changes and loses the all-sentinel barrier request.

The provider receives one atomic update. `stat(5)` assigns rename permission to
the containing directory, length permission to the file, mode/mtime permission to
the owner or current group leader, and gid changes to its specified group rules.
It prohibits toggling DMDIR, changing other protected fields and renaming onto an
existing sibling. Directory length cannot be made nonzero; providers may reject
length changes for additional reasons. Administrative initialization and special
devices can have explicitly different contracts. An acknowledged rejection from
a provider conforming to this contract means none of the requested changes were
made. It does not mean a lost transport reply proves rollback.

Sentinels must not be written as literal field values. Provider-maintained Qid
versions and bookkeeping timestamps may still change as consequences of a
successful mutation according to the provider's documented behavior.

An all-sentinel request must still be dispatched. The provider **may** interpret
it as a stable-storage barrier; Fog must not promise that capability universally.
The syscall propagates the device's result. `mntwstat` returns the submitted
record length after Rwstat; it does not return zero merely because the wire reply
has no payload. Success and failure release the operation's reference but retain
the installed descriptor.

## Remove ownership and provider behavior

`remove(path)` is separate from `NamespaceSession.RemoveAsync(fid)`. The latter
consumes a caller-owned fid; pathname removal owns a fresh lookup channel and
does not invalidate existing descriptor slots. `namec(Aremove)` makes that channel
private specifically because the device remove operation consumes it.

The syscall rejects `ismtpt` before provider removal. A child inside a mounted
tree is removable unless it is itself a mount point. Normal removal requires
write permission on the parent directory and an empty target if it is a directory;
file-content write permission is not required.

Once remove is dispatched and acknowledged, its provider fid is consumed on
**both success and rejection**. `sysremove` changes the channel type to the no-op
root close handler before releasing it, including in its error handler. The
managed equivalent must release local ownership without issuing a second
provider clunk. Before dispatch, including mount-point rejection, temporary lookup
ownership is closed normally. Successful syscall removal returns zero.

Other open handles remain owned by their descriptor slots. Whether IO through
them still works is provider-dependent: native file servers can invalidate them
with a phase error, while Unix-backed providers can retain access until close.
Fog must preserve either documented provider policy rather than impose Unix
unlink semantics. Removing the visible winner of a union can reveal another
member's file; native removal does not add a whiteout or remove every match.

## Async completion, recovery and implementation boundaries

Admission must retain the selected channel before awaiting providers, without
holding process, fd-table or namespace ownership locks across IO. Closing/reusing
an fd or changing a mount cannot redirect an admitted operation. Exit blocks new
operations but retains completion ownership for admitted work. A surviving child
sharing descriptors keeps its ownership. Caller cancellation before dispatch
releases ordinary temporary references; after dispatch, mutation uncertainty must
not be reported as an acknowledged rejection.

Mutation requests need authenticated operation identities and owned immutable
request data. A lost wstat/remove reply must retain its unresolved outcome and
cleanup record. Recovery queries or deduplicates that original operation; it must
not perform a new lookup and accidentally rename or remove a replacement file.
For uncertain remove, a speculative extra clunk is unsafe because the fid may
already have been consumed. These cases extend
[DistributedLifetimes.feature](DistributedLifetimes.feature) and the partial
`NS_DIR_042` contract.

Wstat/fwstat now implement this boundary. `DurableFileStatOperations` persists
the exact raw request, authenticated context, selected resource and optional
retained open handle before dispatch. An ordinary provider or transport failure
leaves the entry pending and raises `WStatRecoveryPendingException`. Recovery
dispatches the saved request under the original operation identity; it never
repeats pathname or descriptor lookup. A definite
`ResourceWStatRejectedException` is journaled as rejected, while success is
journaled with its provider result. `OrleansWStatRecoveryStore` uses a persistent
grain keyed by session epoch. The resource provider must durably deduplicate that
same operation identity and reject reuse with a different request. Pathname remove
still needs its operation-specific journal and consumed-fid rules.

`Plan9FileSyscalls` implements `StatAsync`, `FStatAsync`, `WStatAsync` and
`FWStatAsync`. Descriptors retain the visible last path element, the mount-point
marker captured at open and the provider open handle. `ResourceOpenHandle` carries
the separate CMSG-equivalent mount-transport flag. Path mutation owns its immutable
raw request through completion and dispatches through `IFileStatOperations`;
`FileStatOperations` adapts that request to one typed `IResourceWStatOperations`
call. `DurableFileStatOperations` adds admission and reconciliation without
changing native validation order. Pathname removal remains pending.

## Acceptance and verification

| Scenario range | Evidence required before marking implemented |
| --- | --- |
| `NS_META_001`–`015`, `038`, `040` | Executable path/fd lookup, name rewriting, bounds, error and offset tests against instrumented conforming providers |
| `NS_META_016`–`024` | Structural-validation precedence, name boundaries, sentinel forwarding, mount-point/CMSG differences and provider result tests |
| `NS_META_025`–`029` | Provider conformance tests for permissions, atomic changes, protected fields and advertised durability behavior |
| `NS_META_030`–`031`, `039` | Raw versus transport size boundaries, visible-name overflow and malformed-reply tests; no unsafe count conversion |
| `NS_META_032`–`037` | Barrier-controlled reuse/exit/cancellation/namespace races, immutable request ownership, and durable recovery evidence where required |
| `NS_REMOVE_001`–`007`, `014` | Lookup, mount-point refusal, union selection, fd preservation and no-double-clunk traces on both outcomes |
| `NS_REMOVE_008`–`009` | Provider permissions, empty directories and both documented post-removal handle policies |
| `NS_REMOVE_010`–`013` | Async ownership barriers and lost-reply recovery proving replacement resources are untouched |

Implementation validation must include:

- Executable BDD bindings with explicit scenario-to-test mapping; design files
  alone do not count as passing acceptance tests.
- Property tests for record/string bounds, sentinel round trips, visible-name
  size arithmetic and retained identity/offset invariants across operation sequences.
- Bounded fuzzing of malformed input/replies, multibyte names, size limits and
  update/remove/close/reuse sequences with an independent ownership oracle.
- Real Orleans-provider tests for forwarded metadata, atomic update/rejection,
  remove consumption and serialized errors, in addition to local test doubles.
- The standard quality pipeline and targeted mutation gate with **zero survivors,
  zero uncovered mutants and zero timeouts**. Explicit progress assertions must
  catch lost lock releases and unfinished cleanup before later tests hang.

## Stat/fstat implementation evidence — 2026-09-24

Configure `Plan9FileSyscalls` with `fileStats: new FileStatOperations(resources,
deviceBindings)`. Hosts can instead supply `IFileStatOperations` for a raw stat
provider. The typed adapter uses explicit wire device mappings and requires
`IResourceOpenStatOperations` for fstat; it never falls back from an open handle to
a resource-name lookup. Orleans implements this capability through
`IOpenStatResourceGrain`, which must be the registered resource interface.

Both syscalls return `ReadOnlyMemory<byte>` containing one full raw record or a
two-byte size hint. The returned buffer does not alias provider memory. Fstat pins
the descriptor lease through completion and preserves its saved visible name,
including through duplication and copied descriptor tables. A manually installed
descriptor without a visible name preserves the provider name. Path stat captures
the process root/current directory and mount-table reference together, resolves
the path and final mount, and dispatches once against the selected resource.

Cancellation is checked before admission and again after path lookup. Once stat
is dispatched, provider completion owns its cleanup and runs without the caller's
cancellation token. The syscall does not retry, abandon the provider task, or hold
process/descriptor locks across the provider await. A raw provider needing a
temporary fid must own its allocation and cleanup inside its `StatAsync` operation.
The supplied object/grain adapters allocate no temporary protocol fids. This is
not evidence of durable distributed fid recovery or a full remote 9P stat adapter.

Four executable scenarios in
[`FileStatSyscalls.feature`](../../../NinePSharp.Namespaces.Tests/Features/FileStatSyscalls.feature)
cover final-root mounting without opening, retained aliases across unmount,
two-stage size hints, and metadata through write-only descriptors. Unit/property
tests cover regular-file positions, shared/copied descriptors, current length,
root and absent-path naming, malformed replies, full unsigned record sizes,
UTF-8 replacement arithmetic, unsupported provider capabilities, cancellation,
close/reuse/exit and namespace changes after dispatch. The namespace syscall fuzz
target now includes bounded stat/name/record and duplicate-ownership oracles.
Real Orleans tests cover open-handle dispatch, live length, replacement mounts,
post-removal provider errors and preservation of descriptor ownership.

| Design cases | Current evidence and boundary |
| --- | --- |
| `NS_META_001`–`005`, `007`–`009`, `011`–`024`, `038`, `040` | Metadata paths covered by BDD/unit/property tests, including raw validation precedence, final mounts, retained channels, sentinel forwarding and device results |
| `NS_META_006` | Fstat accepts the transport channel; fwstat rejects only `IsMountTransport`, independently of ordinary mounted files |
| `NS_META_010` | Actual rename preserves an old descriptor's saved visible name while fresh lookup and stat use the new name |
| `NS_META_030`–`031`, `039` | Raw prefix range, growth overflow and malformed stat replies tested; outer Rstat/Twstat encoding remains the transport adapter's responsibility |
| `NS_META_025`–`029` | Memory and real-grain provider tests cover protected fields, collision atomicity, combined rename/mode/truncation and all-sentinel dispatch; production authorization and optional barrier policy remain provider capabilities |
| `NS_META_032`–`034`, `036`–`037` | Cancellation, immutable-buffer, retained-operation and close/reuse/exit race tests cover local completion ownership |
| `NS_META_035` | Durable path and retained-handle reconciliation has unit/property/fuzz coverage plus real two-silo BDD for gateway recreation, provider migration, identity collision and replacement paths |
| `NS_REMOVE_*` | Pathname remove and its consumed-fid lost-reply recovery remain pending |

## Wstat/fwstat implementation evidence — 2026-09-25

`FileStatRecords.ValidateAndCopyUpdate` enforces the pinned `statcheck` framing and
the kernel's limited short-name check before path or descriptor admission. The
owned byte array prevents caller mutation during asynchronous dispatch. The typed
codec preserves exact-width all-ones sentinels, empty-string sentinels and the
full 65,537-byte raw record boundary. Its strict UTF-8 decoding is a documented
managed-provider boundary after native structural validation.

`WStatAsync` performs namespace lookup and the final mount crossing without a
content open. `FWStatAsync` pins the admitted descriptor through provider
completion, accepts every content open mode, preserves its offset/cursor, rejects
only the mount-transport flag, and uses the channel's retained mount-point marker.
Unmount cannot clear that marker and a later mount cannot retroactively set it.
Both syscalls return the provider result and pass `CancellationToken.None` after
dispatch so caller cancellation cannot abandon mutation ownership.

The memory provider and `IWStatResourceGrain` test provider apply one atomic typed
request with idempotent operation identity. Tests cover successful combined rename,
mode and length changes; collision/protected-field rejection without partial
mutation; retained handles; and exact sentinel serialization across Orleans.
Four executable scenarios live in
[`FileWStatSyscalls.feature`](../../../NinePSharp.Namespaces.Tests/Features/FileWStatSyscalls.feature).

Validation commands and measured results are recorded in
[FileSyscallValidation.md](FileSyscallValidation.md). Native syscall conformance,
provider permissions, and durable recovery are tracked independently.
