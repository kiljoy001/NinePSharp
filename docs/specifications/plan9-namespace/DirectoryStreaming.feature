@plan9_namespace @directory_streaming @design_pending
Feature: Native directory streams retain channel state across bounded reads
  This expands NS_IO_011 against the pinned 9front source in DirectoryStreaming.md.
  Scenarios are design specifications pending executable bindings.
  Native cases use conforming stat-record providers and acknowledged device outcomes.
  Async adaptation cases are explicitly tagged and do not assert native cancellation semantics.

  @NS_DIR_001 @native
  Scenario: A directory provider is read through its retained open handle
    Given an open non-union directory with no mounted entries
    And its provider returns a complete 64-byte stat record followed by a complete 72-byte record
    When two implicit reads request 80 bytes each
    Then provider reads use the same handle at offsets 0 and 64
    And the reads return 64 and 72 bytes respectively
    And the shared visible offset and device offset are both 136
    And no whole-directory metadata enumeration is required

  @NS_DIR_002 @native
  Scenario: A successful short read does not imply directory EOF
    Given a directory provider with multiple batches of complete stat records
    When it returns one complete record smaller than the requested count
    Then that record is returned immediately without filling from a later batch
    And the next read continues from the resulting provider offset

  @NS_DIR_003 @native
  Scenario: Dup and copied descriptor tables share a stream but independent opens do not
    Given a directory descriptor duplicated and inherited through a copied descriptor table
    And the same directory is independently opened
    When the original descriptor consumes one provider batch
    Then its duplicate and inherited descriptor observe the advanced stream state
    And the independent open still reads from offset zero with its own handle
    And closing one shared descriptor does not close the shared stream

  @NS_DIR_004 @native
  Scenario: Non-union directory pread validates the visible offset before provider IO
    Given an open non-union directory at visible offset 80 and device offset 64
    And no overflow records are buffered
    When pread requests offset 80
    Then the provider receives offset 64
    And successful pread advances both channel counters by their respective byte counts
    When pread requests a positive offset different from the new visible offset
    Then directory seek fails without another provider read

  @NS_DIR_005 @native
  Scenario: The implicit sentinel and invalid negative offsets are distinguished
    Given an open directory with an established nonzero cursor
    When pread requests offset minus one
    Then it continues at the shared visible position
    When pread requests offset minus two
    Then it fails before consuming overflow or calling a provider

  @NS_DIR_006 @native
  Scenario: Union pread does not validate a positive offset against the visible offset
    Given an open multiple-member union at a nonzero visible offset
    And its active member has a retained stream at member offset 64
    And no overflow records are buffered
    When pread supplies a different positive offset
    Then the active member is read at member offset 64
    And the supplied offset does not reposition the member or overwrite the visible offset
    And successful completion advances the existing visible offset by returned bytes

  @NS_DIR_007 @native
  Scenario: Pread at zero performs directory rewind before reading
    Given an open union with nonzero offsets, an active member and overflow records
    When pread requests offset zero with room for the first record
    Then both outer offsets reset to zero before provider IO
    And overflow is discarded and the active member is released
    And enumeration opens the first current member at member offset zero

  @NS_DIR_008 @native
  Scenario: Absolute-zero seek defers stream cleanup until a read at zero
    Given an open union with nonzero offsets, an active member and overflow records
    When seek sets the absolute offset to zero
    Then the visible offset and union member index are zero
    And seek performs no provider read or immediate active-member clunk
    And the device offset and overflow remain until the next read at zero
    When an implicit read follows
    Then rewind clears overflow, resets the device offset and releases the active member before reading

  @NS_DIR_009 @native
  Scenario Outline: Other directory seeks fail without altering stream state
    Given an open directory with an established cursor and retained stream state
    When seek requests <request>
    Then the seek fails and all stream state is unchanged

    Examples:
      | request                  |
      | absolute one             |
      | absolute minus one       |
      | zero relative to current |
      | zero relative to end     |

  @NS_DIR_010 @native
  Scenario: A zero-count read still follows the native directory read path
    Given an open non-union directory with nonzero offsets
    And its provider acknowledges zero-count reads with zero bytes
    When pread requests zero bytes at offset zero
    Then rewind resets the channel offsets
    And the provider is called at offset zero with count zero
    And the read returns zero bytes

  @NS_DIR_011 @native
  Scenario: Initial union open failure is not hidden by read-time member skipping
    Given a multiple-member union whose first provider rejects the initial directory open
    When the process opens that union for reading
    Then open fails without installing a descriptor
    And later members are not opened as a read-time fallback

  @NS_DIR_012 @native
  Scenario: Union members are cloned and opened lazily for enumeration
    Given a successfully opened multiple-member union A then B
    And A has two nonempty batches before EOF
    When successive directory reads consume those two batches
    Then A has one enumeration clone opened with OREAD
    And reads use that clone at offsets zero and the first batch length
    And B has not been cloned or opened for enumeration
    And the initially opened outer channel remains separately owned

  @NS_DIR_013 @native
  Scenario: EOF advances to the next member within the same read
    Given an open union A then B with an active A stream at EOF
    And B returns a nonempty batch containing a name also returned earlier by A
    When the next directory read is issued
    Then the A enumeration stream is released
    And B is cloned and opened for reading at member offset zero
    And the read returns B's batch without deduplicating that name
    And no records from another nonempty member are appended to that batch

  @NS_DIR_014 @native
  Scenario Outline: A failed union component is skipped and its ownership released
    Given a successfully opened union A then B
    And A's enumeration <operation> fails with an acknowledged provider error
    And B supplies a complete directory record
    When a directory read reaches A
    Then every successfully acquired A enumeration channel is released
    And the same read advances to B and returns its record
    And A's error is not returned as the syscall result

    Examples:
      | operation |
      | clone     |
      | open      |
      | read      |

  @NS_DIR_015 @native
  Scenario: Exhausting failed or empty members returns zero
    Given a successfully opened union whose remaining members fail or return EOF
    When the directory is read
    Then every remaining member is attempted in order
    And all acquired enumeration channels are released
    And the read returns zero with the member index past the current list

  @NS_DIR_016 @native
  Scenario: A non-union provider error is propagated rather than skipped
    Given an open non-union directory with no buffered overflow
    When its provider rejects a read because the buffer cannot fit the next record
    Then the syscall returns that error
    And the outer visible and device offsets do not advance
    And the open directory handle remains owned

  @NS_DIR_017 @native
  Scenario: Opening a single-member mount does not retain union traversal state
    Given a directory opened while its mount head contains only A
    When B is subsequently appended to that mount head
    Then reads through the existing descriptor continue on the original A handle
    And rewind does not turn that descriptor into a multiple-member union stream
    And a new open observes the current A then B union

  @NS_DIR_018 @native
  Scenario: An appended member can be discovered after previous union EOF
    Given a descriptor opened on union A then B
    And enumeration has reached EOF with member index two and no active member
    And its visible offset is nonzero
    When C is appended to that retained mount head
    Then the next implicit read opens C at member offset zero without rewind

  @NS_DIR_019 @native
  Scenario: Removing the active member preserves its stream but changes positional traversal
    Given an open union A then B then C at member index zero
    And the active A stream has returned data and has one further batch before EOF
    When A is selectively unmounted between read calls
    Then the next read can still return the retained A stream's remaining batch
    When the following read reaches A's EOF
    Then A's enumeration stream is released and the index advances to one
    And enumeration opens C from the current B then C list
    And B is not retroactively inserted into this enumeration

  @NS_DIR_020 @native
  Scenario: Prepending a member can cause an existing member to be visited again
    Given an open union A then B at member index zero with an active A stream
    And the visible offset is nonzero
    When X is prepended between read calls
    And the retained A stream subsequently returns EOF
    Then enumeration advances to index one in the current X then A then B list
    And A is cloned and opened again at member offset zero
    And this cursor does not enumerate X unless rewound

  @NS_DIR_021 @native
  Scenario: Complete unmount empties the retained union head without rebinding the descriptor
    Given an open union with a nonzero visible offset and a retained active member
    And no overflow records remain
    When its entire mount head is unmounted between read calls
    And a new mount is installed at the same path
    Then the old descriptor's next read returns zero without reading its retained member
    And it does not traverse the new mount head
    And the retained member is eventually released by rewind or final channel release
    And a new path open sees the new mount

  @NS_DIR_022 @native
  Scenario: Replacement within an existing mount head changes subsequent positional traversal
    Given a descriptor opened on union A then B at member index one with active B
    And the visible offset is nonzero and no overflow remains
    When MREPL replaces that existing head's list with only C between reads
    Then the next read returns zero because index one is beyond the current list
    When an implicit read follows an absolute-zero seek
    Then B's retained stream is released and C is opened at member offset zero

  @NS_DIR_023 @native
  Scenario: Mountfix replaces metadata while preserving the directory entry name
    Given a provider record named visible whose mounted replacement is named actual
    And the original identity is not itself a member of the replacement union
    When the directory record is read with enough room for replacement metadata
    Then its returned name remains visible
    And its other stat fields come from the mounted replacement
    And all UTF-8 string lengths and the total stat length are recomputed consistently

  @NS_DIR_024 @native
  Scenario: Mountfix matches device identity and Qid path rather than entry name or version
    Given two same-named records with distinct device identities or Qid paths
    And only the first identity has a replacement mount
    And the first record's Qid version differs from the mount-point channel version
    When both directory records are read
    Then only the first is replaced despite its changed version
    And the second keeps its provider metadata

  @NS_DIR_025 @native
  Scenario: An original resource retained in a union suppresses metadata rewriting
    Given a provider record whose mount head includes its original resource identity
    And a different resource is first in that union
    When mountfix processes the record
    Then the original record is returned without statting the first member for replacement

  @NS_DIR_026 @native
  Scenario Outline: Failed replacement stat leaves the original record intact
    Given a provider record with a mounted replacement
    When the replacement stat <outcome>
    Then the original complete record is returned unchanged
    And temporary replacement channel and mount-head references are released

    Examples:
      | outcome                             |
      | fails with an acknowledged error    |
      | is too short to contain its size    |
      | cannot supply a complete named stat |

  @NS_DIR_027 @native
  Scenario: A replacement stat larger than the initial scratch buffer is fetched again
    Given a mounted replacement whose stat size plus the original name length exceeds 4096 bytes
    And it reports the required stat length in the first response
    When mountfix reads that replacement with a sufficiently large caller buffer
    Then it retries stat with room for the reported record and original name
    And returns a complete replacement record with the original name

  @NS_DIR_028 @native
  Scenario: Mountfix consults the calling process namespace
    Given parent and child share an open directory channel but have copied namespace groups
    And only the child's namespace replaces the next provider record's identity
    When the child reads that record
    Then the returned metadata reflects the child's replacement
    And the shared channel's provider stream and union-head ownership remain unchanged

  @NS_DIR_029 @native
  Scenario: Growing a record buffers trailing records without splitting them
    Given a provider batch A of 60 bytes and B of 60 bytes
    And A's replacement with the original name is 100 bytes
    When a directory read requests 120 bytes
    Then it returns only the complete 100-byte replacement A
    And the original 60-byte B record is buffered as overflow
    And the outer device offset advances by 120 while the visible offset advances by 100
    When the next read requests 120 bytes and B has no replacement
    Then it returns B from overflow without another provider read
    And the device offset becomes 180 and the visible offset becomes 160

  @NS_DIR_030 @native
  Scenario: Multiple overflow evictions retain the source implementation's tail order
    Given a provider batch A then B then C with each record occupying 60 bytes
    And A's replacement occupies 150 bytes
    When a directory read requests 180 bytes
    Then it returns replacement A and buffers original C followed by original B
    When the next read has room for both overflow records and neither has a replacement
    Then the returned order is C then B without a provider read

  @NS_DIR_031 @native
  Scenario: A replacement larger than the caller buffer can return zero with pending overflow
    Given one provider record A of 60 bytes with a replacement of 100 bytes
    When a directory read requests 80 bytes
    Then it returns zero and buffers original A
    And the device offset is 60 and the visible offset is zero
    When a larger implicit read follows
    Then reading at visible offset zero discards that overflow and rewinds before provider IO

  @NS_DIR_032 @native
  Scenario: Buffered records are reconsidered against current mounts
    Given a nonzero visible position with a buffered original record B
    When B's replacement mount changes between read calls
    And the next read can hold the rewritten B
    Then B is consumed from overflow without a provider read
    And mountfix uses the current replacement metadata

  @NS_DIR_033 @native
  Scenario: An overflow read takes priority over nonzero pread offset validation
    Given an open non-union directory at a nonzero visible offset with buffered records
    When pread supplies a different positive offset and a buffer that fits the first buffered record
    Then buffered data is returned without a provider read or directory-seek error
    And the existing outer counters advance by the raw and rewritten buffered lengths

  @NS_DIR_034 @native
  Scenario: An undersized overflow buffer falls through to the provider path
    Given a non-union directory at a nonzero visible offset with a buffered 100-byte record
    And its provider will acknowledge the next read with zero bytes
    When an implicit read requests 80 bytes
    Then the buffered record remains unconsumed
    And the provider is called at the current device offset with count 80
    And that read returns zero

  @NS_DIR_035 @native
  Scenario: Overflow can still be returned after complete union unmount
    Given an open union at a nonzero visible offset with buffered records
    When its entire mount head is unmounted between reads
    And the next read can fit a buffered record
    Then buffered data is returned before the empty retained union head is consulted
    And the read does not acquire a member from a replacement mount

  @NS_DIR_036 @native
  Scenario: Final channel release cleans up the outer channel and active member
    Given a shared directory channel with an outer provider handle, active member and overflow
    When its final descriptor and admitted-operation references are released
    Then both successfully opened provider handles are eventually closed once
    And overflow storage and retained mount-head ownership are released
    And no released state can be reused by another descriptor allocation

  @NS_DIR_037 @native
  Scenario: An acknowledged close error does not preserve stream ownership
    Given a directory channel at final release whose provider close reports an error
    When final channel cleanup runs
    Then local ownership of that provider handle is still discarded
    And active-member, overflow and mount-head cleanup still proceeds
    And an already invalidated provider fid is not reused or retried as a fresh close

  @NS_DIR_038 @async_adaptation
  Scenario: A cancelled cursor waiter does not change stream state
    Given a directory read suspended in provider IO while holding the async cursor lease
    When another read or rewind waits for that lease and is cancelled before admission
    Then the waiter performs no provider IO and changes no cursor or overflow state
    And the admitted read retains its channel until completion

  @NS_DIR_039 @async_adaptation
  Scenario: Same-head mutation waits for an admitted union provider step
    Given a union read retaining its member and an async read lease on its mount head
    And the member provider operation is suspended
    When selected unmount or replacement requests exclusive access to that head
    Then the mutation waits until the union step releases its read lease
    And unrelated mount heads and descriptor tables remain usable
    And no process, descriptor-table or synchronous namespace lock is held across the provider await

  @NS_DIR_040 @async_adaptation
  Scenario: Close and process exit do not redirect a pending directory operation
    Given an admitted directory read retaining its outer channel and active member
    When its descriptor is closed, its slot is reused and its process exits
    And the already admitted provider step completes
    Then completion belongs only to the retained original channel
    And the replacement descriptor's position and contents are unchanged
    And final reference release schedules all remaining stream cleanup once

  @NS_DIR_041 @async_adaptation
  Scenario: A late member-open result after descriptor closure still has a cleanup owner
    Given a directory read awaiting a member open with retained channel ownership
    When every descriptor for that channel closes
    And the provider returns a successfully opened member handle
    Then the admitted read retains ownership of the returned handle until it completes
    And final channel release closes that handle even if no descriptor can receive another call

  @NS_DIR_042 @async_adaptation
  Scenario: Uncertain member-open completion is not treated as a definite skip
    Given an admitted member open whose provider reply is lost
    When the caller cancels or times out without an acknowledged provider outcome
    Then the uncertain open is not classified as a native failed component
    And no automatic replay or next-member progression assumes it acquired no handle
    And a completion owner retains the operation identity for resolution and cleanup

  @NS_DIR_043 @async_adaptation
  Scenario: Rewind is serialized with pending directory reads
    Given a directory read suspended while owning the cursor lease
    When another descriptor sharing that channel requests rewind
    Then rewind waits without synchronously blocking the Orleans scheduler
    And the read commits its result or failure before rewind changes cursor state
    And the following read at zero performs the native stream reset

  @NS_DIR_044 @provider_contract
  Scenario Outline: Invalid provider directory bytes are rejected at the adapter boundary
    Given a directory provider response containing <defect>
    When the streaming adapter validates that response
    Then no malformed stat record is returned to the application
    And the error preserves cleanup ownership without an implicit retry

    Examples:
      | defect                                      |
      | a truncated record length prefix            |
      | a record length extending past the response |
      | a string length extending past its record   |
      | more bytes than the requested count         |
