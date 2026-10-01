@plan9_namespace @metadata_syscalls @design_pending
Feature: Processes inspect and change metadata through namespace paths and retained channels
  References and source/manual differences are recorded in MetadataSyscalls.md.
  Design scenarios map to stat/fstat evidence in MetadataSyscalls.md.
  Stat/fstat/wstat/fwstat have local bindings; wstat/fwstat also have durable distributed recovery.
  Native cases assume conforming providers and acknowledged outcomes.
  Provider rules and asynchronous host adaptations have separate tags.

  @NS_META_001 @native
  Scenario Outline: Path metadata calls resolve without opening a file descriptor
    Given a process with its own root and current directory
    And a visible file reached through ordered union lookup and a final mount crossing
    When the process calls <operation> on that path
    Then the operation uses the resource selected by its namespace
    And no provider open or process descriptor allocation occurs
    And the temporary lookup channel is released after completion

    Examples:
      | operation |
      | stat      |
      | wstat     |

  @NS_META_002 @native
  Scenario: Stat of a union root uses its first mounted resource
    Given a union root with mounted resources A and B
    When stat inspects the union root itself
    Then it returns A's metadata with the visible channel name
    And it neither merges metadata nor enumerates union directory entries
    And a stat failure from A is not retried against B

  @NS_META_003 @native
  Scenario Outline: Descriptor metadata targets the retained channel after namespace changes
    Given a descriptor opened through a mount onto resource A
    When the mount is removed and its old pathname selects resource B
    And the process calls <operation> on that descriptor
    Then the provider operation still targets A through the retained channel
    And a fresh path metadata call selects B

    Examples:
      | operation |
      | fstat     |
      | fwstat    |

  @NS_META_004 @native
  Scenario Outline: Descriptor metadata does not require read or write open mode
    Given a descriptor opened with <mode>
    And its provider authorizes the requested metadata operation
    When fstat and fwstat use that descriptor
    Then both reach the provider without a syscall open-mode rejection
    And fwstat permissions are decided by the provider

    Examples:
      | mode   |
      | OREAD  |
      | OWRITE |
      | ORDWR  |
      | OEXEC  |

  @NS_META_005 @native
  Scenario Outline: Invalid descriptors do not reach the metadata provider
    Given a well-formed metadata request
    When <operation> uses a negative or unallocated descriptor
    Then the syscall fails with a bad-descriptor error
    And no provider metadata call occurs

    Examples:
      | operation |
      | fstat     |
      | fwstat    |

  @NS_META_006 @native
  Scenario: Fstat and fwstat differ on a mount transport descriptor
    Given a descriptor whose retained channel has the native CMSG mount-transport flag
    When fstat uses that descriptor
    Then the stat request reaches the transport channel's provider
    When fwstat uses that descriptor with a valid request
    Then it fails with bad use of file descriptor before provider mutation
    And an ordinary file opened through a mount is not treated as a CMSG transport

  @NS_META_007 @native
  Scenario: Metadata operations preserve shared offsets and directory cursor state
    Given duplicated and copied descriptors sharing an open channel
    And an established file position or native directory stream with buffered overflow
    When fstat or an acknowledged fwstat completes or fails
    Then the syscall does not change the shared offset
    And it does not rewind, consume or invalidate the directory stream buffers
    And provider changes to file length do not clamp the retained file offset

  @NS_META_008 @native
  Scenario: Stat returns current provider metadata rather than an open-time snapshot
    Given an open file whose provider changes its length, version and modification time
    When fstat is called again
    Then it returns the current provider values
    And the provider resource identity is preserved independently of the visible name

  @NS_META_009 @native
  Scenario Outline: Stat names follow the retained channel path
    Given a provider stat record named "provider-name"
    And the selected channel has <path>
    When stat or fstat returns a complete record
    Then its name is <name>
    And all other provider metadata fields and strings remain unchanged

    Examples:
      | path                         | name            |
      | retained path /alias/report  | report          |
      | retained path /              | an empty string |
      | no retained path             | provider-name   |

  @NS_META_010 @native
  Scenario: Renaming does not rewrite an existing channel path
    Given an open channel with retained path /data/old
    When fwstat successfully renames its provider file to new
    Then fresh lookup finds /data/new and no longer finds /data/old
    And fstat of the original descriptor still rewrites the name to old
    And a fresh stat of /data/new returns the name new

  @NS_META_011 @native
  Scenario: Stat uses one complete little-endian directory entry
    Given provider metadata with distinct type, device, Qid, mode, times and length
    And multibyte UTF-8 name, owner, group and last-modifier strings
    When stat returns a complete record into a sufficiently large buffer
    Then its leading unsigned 16-bit count excludes the count's own two bytes
    And string lengths count encoded bytes rather than characters
    And the result is the complete record byte count without 9P message framing

  @NS_META_012 @native
  Scenario Outline: A bounded stat result advertises insufficient space
    Given a mounted 9P provider with a stable complete stat size of 80 bytes
    And the retained channel name has the same encoded length as the provider name
    When stat or fstat requests <capacity> bytes
    Then the result is <result>

    Examples:
      | capacity | result                                         |
      | 0        | a short-stat error before the provider request |
      | 1        | a short-stat error before the provider request |
      | 2        | two bytes containing the unsigned count 78     |
      | 79       | two bytes containing the unsigned count 78     |
      | 80       | the complete 80-byte record                    |
      | 100      | the complete 80-byte record                    |

  @NS_META_013 @native
  Scenario: A provider size-only reply precedes visible-name rewriting
    Given a provider record of 80 bytes and a visible name 12 bytes longer
    When stat requests two bytes
    Then it returns two bytes containing count 78 without adjusting the name
    When stat is retried with 80 bytes
    Then it returns two bytes containing count 90 after calculating the name replacement
    When stat is retried with 92 bytes
    Then it returns the complete 92-byte record with the visible name
    And the syscall itself performs no hidden retry

  @NS_META_014 @native
  Scenario: A shorter visible name only helps after the provider record fits
    Given a provider record of 80 bytes and a visible name 12 bytes shorter
    When stat requests 68 bytes
    Then it returns the provider's two-byte size hint for 80 bytes
    When stat requests 80 bytes
    Then it returns a complete rewritten record of 68 bytes with count 66

  @NS_META_015 @native
  Scenario: Provider stat failure releases the operation reference
    Given a valid path or descriptor selected for stat
    When the provider rejects the metadata request
    Then the error is propagated without a union-member fallback
    And the temporary channel or descriptor lease is released
    And the caller's existing descriptor remains installed

  @NS_META_016 @native
  Scenario Outline: Wstat validates record structure before resolving its target
    Given an invalid path or unallocated descriptor
    And a wstat record with <defect>
    When wstat or fwstat is called
    Then it fails with bad stat before path lookup or descriptor admission
    And no provider mutation occurs

    Examples:
      | defect                                      |
      | fewer than the 49 fixed bytes                |
      | a size prefix unequal to supplied length - 2 |
      | a truncated string-length field              |
      | a string extending beyond the buffer         |
      | bytes left over after the fourth string      |

  @NS_META_017 @native
  Scenario Outline: Short wstat names receive the kernel's limited validation
    Given a structurally valid wstat record with <name>
    And a non-mount-point target
    When wstat or fwstat validates the request
    Then <outcome>

    Examples:
      | name                                     | outcome                                    |
      | an empty name                            | name validation permits provider dispatch  |
      | the single slash /                       | name validation permits provider dispatch  |
      | dot                                      | name validation permits provider dispatch  |
      | dot dot                                  | name validation permits provider dispatch  |
      | a 63-byte name containing a slash         | the kernel rejects the name                |
      | a short name containing nonzero ASCII 31  | the kernel rejects the name                |
      | a short name containing ASCII 127         | the kernel rejects the name                |
      | a 64-byte name containing a slash         | name validation is deferred to the provider |

  @NS_META_018 @native
  Scenario: Wstat forwards unchanged-field sentinels without a preliminary stat
    Given a well-formed record initialized like nulldir with mode set to 0600
    When wstat or fwstat submits the update
    Then metadata numeric fields other than mode contain their width-specific all-ones values
    And unchanged strings are empty
    And the provider receives one update without a read-modify-write stat sequence

  @NS_META_019 @native
  Scenario: An all-sentinel update still reaches the provider
    Given a well-formed wstat record containing only unchanged-field sentinels
    When wstat or fwstat submits it
    Then the syscall dispatches the request and waits for its provider result
    And it does not optimize the request into a local success

  @NS_META_020 @native
  Scenario Outline: Mount-point metadata permits updates but forbids a nonempty name
    Given a selected channel marked as a mount point
    When wstat or fwstat submits <request>
    Then <outcome>

    Examples:
      | request                                  | outcome                                         |
      | a valid mode change with an empty name    | the update reaches the mounted target provider  |
      | the same nonempty name already displayed | mount-point rename is rejected before dispatch  |
      | a different nonempty name                | mount-point rename is rejected before dispatch  |

  @NS_META_021 @native
  Scenario: Fwstat retains the channel's mount-point marker after unmount
    Given a descriptor opened on a mount point
    When that mount is removed
    And fwstat supplies a nonempty name on the retained descriptor
    Then the rename still fails as a mount-point rename
    And an empty-name update can still reach the retained provider

  @NS_META_022 @native
  Scenario: A later mount does not retroactively mark an existing descriptor
    Given a descriptor opened on an ordinary unmounted directory
    When a different resource is mounted on that pathname
    Then path wstat rejects a nonempty rename on the new mount point
    And fwstat on the old descriptor still reaches its original provider for that rename

  @NS_META_023 @native
  Scenario: Failed union-member updates do not mutate another member
    Given a union in which resource A wins lookup of a name also present in B
    When wstat on that name is rejected by A
    Then the rejection is propagated without trying B
    And both the namespace mount list and B's metadata remain unchanged

  @NS_META_024 @native
  Scenario: Wstat returns its device result and retains the caller's descriptor
    Given a well-formed 80-byte update to a mounted 9P file
    When the server acknowledges Twstat
    Then wstat or fwstat returns the device result of 80
    And fwstat neither consumes nor closes the installed descriptor
    And the temporary operation reference is released on both success and rejection

  @NS_META_025 @provider_contract
  Scenario: File metadata can be inspected without file-content read permission
    Given an attached principal that can walk to a file but cannot read its contents
    When stat requests its metadata
    Then stat itself requires no additional file permissions
    And ordinary traversal permissions still apply

  @NS_META_026 @provider_contract
  Scenario Outline: Provider authorization controls mutable metadata
    Given a normal file server enforcing stat protocol permissions
    When a caller requests <change>
    Then the provider requires <authority>

    Examples:
      | change | authority                                                          |
      | name   | write permission on the containing directory                        |
      | length | file write permission and provider support for the length change    |
      | mode   | ownership or leadership of the current group                        |
      | mtime  | ownership or leadership of the current group                        |
      | gid    | ownership plus membership in the new group, or leadership of both groups |

  @NS_META_027 @provider_contract
  Scenario Outline: Provider metadata constraints reject forbidden changes atomically
    Given a normal file server outside its administrative initialization mode
    When one wstat request combines a permitted mode change with <change>
    Then the provider rejects the entire request and the mode remains unchanged

    Examples:
      | change                                  |
      | a rename to an existing sibling name     |
      | a nonzero directory length               |
      | a change to the DMDIR bit                 |
      | a change to the owner uid                |
      | a change to the last-modifier muid       |
      | a change to atime                        |
      | a change to Qid, type or device identity |

  @NS_META_028 @provider_contract
  Scenario: Successful combined updates change all requested fields together
    Given a provider supporting an authorized rename, mode change and truncation
    When it acknowledges a single wstat containing those changes
    Then all three changes are visible
    And no unchanged-field sentinel is applied as a literal replacement value
    And the syscall has not split the request into separate mutations

  @NS_META_029 @provider_contract
  Scenario: A provider may use an all-sentinel request as a durability barrier
    Given a provider advertising all-sentinel wstat as a stable-storage barrier
    When the request is issued while the file has pending durable writes
    Then success is not acknowledged until the provider's barrier is satisfied
    And a provider without that capability does not acquire a durability guarantee from the syscall alone

  @NS_META_030 @provider_contract
  Scenario: Transport stat size limits are distinct from raw record size limits
    Given a valid raw stat record with a 65535-byte payload plus its two-byte prefix
    When a raw adapter measures the record for transport
    Then its raw length is 65537 bytes and must not wrap in managed size arithmetic
    But a 9P2000 Rstat or Twstat adapter cannot encode it in the outer 16-bit stat length
    And the adapter rejects it before dispatch rather than truncating or splitting it

  @NS_META_031 @provider_contract
  Scenario: Malformed stat replies cannot become successful metadata results
    Given a provider reply with an invalid record boundary, string bound or reported count
    When the managed adapter validates the stat reply
    Then it rejects the reply without exposing partial metadata as a complete record
    And it releases the operation reference without consuming the caller's descriptor

  @NS_META_032 @async_adaptation
  Scenario Outline: Descriptor reuse cannot redirect an admitted metadata operation
    Given <operation> admitted on descriptor three and blocked in provider A
    When descriptor three is closed and reused for provider B
    And A completes
    Then the result and any mutation belong only to A
    And A remains retained until its admitted operation releases its lease
    And B's descriptor and metadata are unaffected

    Examples:
      | operation |
      | fstat     |
      | fwstat    |

  @NS_META_033 @async_adaptation
  Scenario: Exit fences new metadata work while admitted work owns its completion
    Given a process with admitted path and descriptor metadata operations awaiting providers
    When the process exits while a child shares its descriptor group
    Then new metadata calls for the exited process are rejected
    And admitted operations retain their original channels through completion or resolution
    And their cleanup does not release the surviving child's ownership
    And termination and descriptor ownership locks are not held across provider waits

  @NS_META_034 @async_adaptation
  Scenario: Cancellation before admission makes no provider request
    Given a metadata call cancelled before it acquires its operation ownership
    When the host processes the cancellation
    Then no provider metadata request is dispatched
    And no temporary channel or descriptor lease is leaked

  @NS_META_035 @async_adaptation
  Scenario: An unknown mutation outcome is not an acknowledged rejection
    Given wstat or fwstat sent with an authenticated operation identity
    When cancellation or a lost reply leaves the provider outcome unknown
    Then the host retains completion and cleanup ownership under that identity
    And it does not retry under a new identity or attempt a rollback
    And recovery resolves the original outcome before declaring failure or releasing unresolved ownership

  @NS_META_036 @async_adaptation
  Scenario: A path operation keeps the resource selected before provider dispatch
    Given stat or wstat has resolved and retained provider A through a path
    When the caller changes its namespace so that a fresh lookup selects B
    And the original provider operation completes
    Then it completes against A without repeating lookup through B
    And its temporary reference is released even after process exit

  @NS_META_037 @async_adaptation
  Scenario: The host owns an immutable wstat request during asynchronous dispatch
    Given a valid wstat request submitted from a mutable caller buffer
    When the caller changes that buffer while the provider operation is pending
    Then the dispatched request retains the bytes validated at admission
    And later caller writes cannot change the provider's requested update

  @NS_META_038 @native
  Scenario: Lookup errors are preserved without fabricating missing metadata
    Given an absent name or a path requiring traversal through a non-directory
    When stat or wstat resolves that path
    Then lookup fails without opening, creating or updating a resource
    And acquired temporary lookup references are released

  @NS_META_039 @provider_contract
  Scenario: Visible-name growth cannot wrap the raw record length
    Given a complete provider stat record whose replacement visible name would exceed a 65535-byte payload
    When the managed syscall adapter calculates the replacement record size
    Then it rejects the unrepresentable result without a wrapped size hint or partial successful record
    And it releases its temporary reference without consuming an installed descriptor

  @NS_META_040 @native
  Scenario Outline: Zero-element path lookup still applies the current final mount
    Given a process retaining its root and current directory before a mount change
    And resource B is now mounted on the directory selected by <path>
    When stat or empty-name wstat is called on <path>
    Then the provider operation targets B after the final mount crossing
    And no content open is needed to discover the current mount

    Examples:
      | path |
      | /    |
      | .    |
