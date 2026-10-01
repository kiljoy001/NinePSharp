# Metadata-backed directory syscall profile

`Plan9FileSyscalls` now supports directories using the existing
`INamespaceDataPlane.ReadDirectoryAsync` metadata interface. This is a first directory
slice, not full native `Chan` directory-stream parity.

The next native slice is specified in [DirectoryStreaming.feature](DirectoryStreaming.feature)
and [DirectoryStreaming.md](DirectoryStreaming.md). Its source-backed scenarios include
differences from this adapter in zero-count reads, deferred seek cleanup, offset
validation, union mutation and overflow handling. The opt-in provider-stream mode
now implements that separate contract with executable coverage; this document
continues to describe the default metadata adapter.

## Observable behavior

- Reads emit standard 9P2000 stat records, including their two-byte length prefixes.
  Each response fits the requested count and contains whole records. A nonzero
  buffer smaller than the next record fails without consuming that record; a zero
  count returns no data without fetching the provider listing.
- Each successful directory open/create has an independent cursor. Dup and copied
  descriptor tables retain the same cursor; descriptor renumber moves its ownership.
- Implicit reads advance the directory's shared byte position. Directory pread also
  advances it, unlike regular-file positioned reads. The accepted offsets are -1
  (implicit), zero (refresh), or the current visible byte offset.
- Seek accepts only absolute zero for directories. Rewind drops the cached metadata
  and resets the record index and visible byte position. Reads at an implicit position
  of zero refresh too, so an empty directory can reveal subsequently created entries.
- A listing preserves the data plane's visible entry order, including duplicate names
  across union members. Mounted-entry metadata keeps the visible entry name.
- Concurrent reads and rewind wait asynchronously on a per-open cursor gate. They
  retain descriptor leases, so close/reuse cannot redirect an admitted read. Cancelling
  a waiter does not consume directory records. Provider failure releases the gate.
- The stat encoder validates UTF-8 size before conversion to the existing `Stat.Size`
  representation. Records exceeding 65,535 total bytes are rejected.

## Source comparison and limits

The reference is the local `../9front` checkout at
`9654fe7fa882f8043c267bbeb6679ebd9102209b`, particularly:

- `sys/man/5/read`: directory reads contain integral stat records and use zero or
  the preceding read's ending offset.
- `sys/src/9/port/sysfile.c:read`: directory pread updates the channel position;
  reading at zero rewinds directory state.
- `sysfile.c:sseek`: absolute zero is accepted for directories; other directory
  seeks fail. This follows source where `seek(2)` describes a broader prohibition.
- `sysfile.c:unionread`, `unionrewind`, `mountfix`, `mountrockread`: reference for
  the remaining native streaming work.

Current providers return whole metadata listings. The adapter caches one listing per
enumeration and bounds only the encoded response, not provider enumeration memory.
Rewind fetches the current listing and mount view. This snapshot policy and the
per-cursor serialization are explicit adapter choices.

Remaining native behavior includes separate provider/visible offsets, per-member
union handles and clunks, skipping failed union members, mountfix overflow buffers,
and live union mutations during enumeration. Native union pread can ignore a nonzero
supplied offset in cases where this profile rejects it. Native type/dev identity is
also not fully represented; encoded type/dev fields use the existing gateway's zero
values. The current `Stat.Size` representation is narrower than the largest theoretical
9P record. None of these limitations is counted as completed `NS_IO_011` parity.

The existing 9P fid dispatcher is unchanged. These cursors serve the virtual-process
syscall API; WASI directory-cookie translation remains future work.

Executable evidence lives in `Features/DirectoryCursors.feature`,
`DirectoryCursorTests`, the `namespace-syscalls` fuzz target, and the Orleans
resource-grain directory syscall test.
