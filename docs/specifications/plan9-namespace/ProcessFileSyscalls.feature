@plan9_namespace @design
Feature: Virtual processes perform file syscalls through retained channels
  Native rules follow 9front sysfile.c and the open, read, seek and dup manuals.
  Distributed admission/completion fencing is a Fog extension.
  These scenarios require executable bindings before they count as implemented.

  @NS_IO_001
  Scenario: Open resolves the process namespace and allocates the lowest free fd
    Given a process whose current directory contains a union-mounted data directory
    When it opens data/report for reading
    Then lookup follows the process root, current directory and ordered union members
    And the lowest free descriptor owns the opened provider channel
    And a later independent open has an independent file position

  @NS_IO_002
  Scenario Outline: File operations enforce the opened access mode
    Given a regular file opened with <mode>
    When the process attempts <operation>
    Then the result is <result>

    Examples:
      | mode   | operation | result   |
      | OREAD  | read      | allowed  |
      | OREAD  | write     | rejected |
      | OWRITE | read      | rejected |
      | OWRITE | write     | allowed  |
      | ORDWR  | read      | allowed  |
      | ORDWR  | write     | allowed  |
      | OEXEC  | read      | allowed  |
      | OEXEC  | write     | rejected |

  @NS_IO_003
  Scenario: Native create truncates an existing writable file
    Given a writable existing file with contents and metadata
    When native create succeeds without OEXCL
    Then its length is zero and its owner, group and permissions are unchanged
    And a new provider open channel is installed in the descriptor table

  @NS_IO_004
  Scenario: Exclusive create is atomic and preserves existing data on collision
    Given two processes creating the same absent name with OEXCL
    When their creates overlap at the provider
    Then exactly one create succeeds
    And the other create fails without truncating the winner's file
    And the API preserves OEXCL beyond a byte-sized 9P open mode

  @NS_IO_005
  Scenario: Descriptor allocation failure releases a successful provider open
    Given a process with no free descriptor capacity
    When the provider completes a create successfully
    Then descriptor publication fails and the returned provider handle is closed once
    And the syscall does not promise to undo the created file

  @NS_IO_006
  Scenario: Short sequential transfers update shared position by actual bytes
    Given duplicated and copied descriptors for one open regular-file channel
    When sequential writes request eight bytes and transfer three
    Then the shared position advances by three
    And the next sequential read through another descriptor starts at that position
    And an EOF read returns zero without advancing the position

  @NS_IO_007
  Scenario: Explicit regular-file IO leaves shared position unchanged
    Given an open regular file with a shared position of twelve
    When pread or pwrite succeeds with explicit offset three
    Then the shared position remains twelve
    When the native positioned API receives the all-ones offset sentinel
    Then it uses the implicit-position operation as 9front does

  @NS_IO_008
  Scenario: A failed implicit write releases its reserved position
    Given an open regular file with position five
    When an implicit write reserves eight bytes and the provider fails
    Then its eight-byte reservation is removed
    And its channel lease is released without closing another descriptor's ownership

  @NS_IO_009
  Scenario: Overlapping native implicit writes reserve separate ranges
    Given an open regular file at position zero
    When an eight-byte write is pending and a four-byte write is admitted
    Then their provider offsets are zero and eight
    And no descriptor-table ownership lock is held across either provider wait
    And short or failed completions apply the 9front offset corrections

  @NS_IO_010
  Scenario Outline: Native seek follows checked-in 9front behavior
    Given an open <resource>
    When seek requests <request>
    Then the result is <result>

    Examples:
      | resource     | request                          | result                         |
      | regular file | zero relative to end             | current file size              |
      | regular file | a negative resulting position    | error without position change  |
      | directory    | absolute zero                    | rewind accepted                |
      | directory    | absolute one                     | error                          |
      | directory    | zero relative to current         | error                          |
      | pipe         | absolute zero                    | stream error                   |

  @NS_IO_011
  # Expanded by NS_DIR_001 through NS_DIR_044 in DirectoryStreaming.feature.
  # Ordinary member traversal preserves duplicates; native mountrock tail eviction
  # and live mount-list changes have the explicit exceptions specified there.
  Scenario: Directory reads retain native cursor and union behavior
    Given an open union directory with duplicate names across members
    When it is read in bounded buffers and then rewound
    Then entries follow member order without deduplicating names
    And the provider and visible directory offsets remain distinct
    And native directory records are not split into invalid stat records
    And rewind resets union and mount traversal buffers

  @NS_IO_012
  Scenario: Exit racing an open cannot publish through a surviving shared group
    Given a process and child sharing one descriptor group
    And a provider open admitted for the parent is held at a completion barrier
    When the parent exits while the child remains active
    And the provider returns a successful open
    Then no descriptor is installed on behalf of the exited parent
    And its returned provider handle is closed once
    And the child's existing descriptors remain usable

  @NS_IO_013
  Scenario: Closing and reusing a slot cannot redirect admitted IO
    Given an admitted read holding a lease for descriptor three
    When descriptor three is closed and reused for a different file
    And the original read completes
    Then it reads the originally retained channel
    And completion cannot update the replacement descriptor or process incarnation
    And the original channel is closed after its final lease is released

  @NS_IO_014
  Scenario: Cancelling async IO waits for acknowledgement or records uncertainty
    Given an admitted provider write whose effect is not yet known
    When process termination requests cancellation
    Then cleanup retains the operation identity until completion or acknowledged abort
    And a lost reply does not authorize replay under a fresh operation identity
    And cleanup failure remains observable for reconciliation

  @NS_IO_015
  Scenario: Ordinary create retries a definite provider rejection
    Given two ordinary creates that both observed the name as absent
    When the provider creates the first file and rejects the second create
    Then the second syscall looks up the visible name again and opens it with OTRUNC
    And a rejection followed by a missing name preserves the original create error
    And no truncating fallback occurs for an uncertain transport failure

  @NS_IO_016
  Scenario: Final process exit during provider creation does not leak the returned handle
    Given a provider create held before returning its successful open handle
    When the last process owning the namespace exits
    And the provider returns the created channel
    Then namespace mount resolution is not repeated on that channel
    And descriptor publication fails and closes the returned handle once
    And ordinary cleanup does not promise to remove the created name
