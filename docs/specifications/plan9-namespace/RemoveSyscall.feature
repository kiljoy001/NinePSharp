@plan9_namespace @remove_syscall @design_pending
Feature: Path removal owns a temporary channel and consumes its provider fid
  References and source/manual differences are recorded in MetadataSyscalls.md.
  These are design scenarios pending implementation and executable bindings.
  Remove is a pathname syscall and is distinct from consuming a caller-owned 9P fid.

  @NS_REMOVE_001 @native
  Scenario: Remove resolves a private channel without allocating a descriptor
    Given a file reachable through the process root, current directory and namespace
    When remove resolves its pathname
    Then it obtains a private temporary channel for the selected provider resource
    And it calls provider remove without first opening the file for content IO
    And no process descriptor is allocated or removed

  @NS_REMOVE_002 @native
  Scenario: Removing a mount point is rejected before provider removal
    Given a resource mounted on a visible pathname
    When remove targets the mount point itself
    Then it fails with a mount-point error before calling provider remove
    And both the mounted resource and hidden mount-point resource remain unchanged
    And the temporary lookup channel is released normally

  @NS_REMOVE_003 @native
  Scenario: Being inside a mounted tree does not make every child a mount point
    Given an ordinary unmounted child inside a mounted directory
    When remove targets that child
    Then its provider receives the remove request
    And the parent mount binding remains installed

  @NS_REMOVE_004 @native
  Scenario: Removal uses lookup order rather than the union create member
    Given a union where both A and B contain report and A wins lookup
    And only B has the MCREATE flag
    When remove targets report and A acknowledges removal
    Then only A's report is removed
    And fresh lookup may expose B's report under the same visible name
    And no whiteout is created

  @NS_REMOVE_005 @native
  Scenario: A selected member's remove failure does not fall through the union
    Given a union where A wins lookup of a name also present in B
    When A rejects removal
    Then the syscall reports that failure without trying B
    And the namespace mount list and B's file are unchanged

  @NS_REMOVE_006 @native
  Scenario Outline: Provider removal consumes its temporary fid on either acknowledged outcome
    Given a private temporary channel backed by a 9P fid
    When the server <outcome> its Tremove request
    Then that fid is consumed and cannot be reused
    And local channel ownership is released without a second Tclunk for the same fid
    And the syscall returns <result>

    Examples:
      | outcome | result                    |
      | accepts | zero                      |
      | rejects | the provider remove error |

  @NS_REMOVE_007 @native
  Scenario: Lookup failure does not issue a remove request
    Given a missing name or a path requiring traversal through a non-directory
    When remove resolves that path
    Then it propagates the lookup error without creating or opening a file
    And all temporary lookup references are released

  @NS_REMOVE_008 @provider_contract
  Scenario Outline: Removal requires parent write permission and an empty directory
    Given a normal file server and <target>
    When an attached principal with <permission> requests removal
    Then the provider <outcome>
    And the request's temporary fid is consumed even on rejection

    Examples:
      | target              | permission                       | outcome                         |
      | a read-only file    | write on its containing directory | removes the file                |
      | a writable file    | no write on its containing directory | rejects without removing it   |
      | an empty directory | write on its containing directory | removes the directory           |
      | a nonempty directory | write on its containing directory | rejects without removing it   |

  @NS_REMOVE_009 @provider_contract
  Scenario Outline: Other open handles follow the provider's post-removal semantics
    Given a file independently opened on an existing process descriptor
    And its provider implements <policy>
    When path remove succeeds using a separate temporary channel
    Then the existing descriptor remains installed with its original handle
    And subsequent IO on that handle <outcome>

    Examples:
      | policy                         | outcome                                  |
      | immediate invalidation         | fails with the provider's phase error    |
      | retained access until final close | continues according to provider policy |

  @NS_REMOVE_010 @async_adaptation
  Scenario: A namespace change cannot redirect a dispatched remove
    Given remove has resolved and retained resource A
    When a mount change makes fresh lookup of the same pathname select B
    And the provider acknowledges the original removal
    Then only A is affected by that request
    And B receives no remove request

  @NS_REMOVE_011 @async_adaptation
  Scenario: Exit retains ownership of an admitted remove until its outcome is known
    Given an admitted remove waiting for its provider
    When the calling process exits
    Then new operations for that process are rejected
    And the admitted remove keeps its operation identity and temporary ownership
    And acknowledgement releases that ownership without a duplicate provider clunk
    And process and namespace ownership locks are not held across the provider wait

  @NS_REMOVE_012 @async_adaptation
  Scenario: A lost remove reply cannot cause removal of a replacement file
    Given a remove request for resource A sent with an authenticated operation identity
    When its reply is lost and the same pathname is later occupied by resource B
    Then the host records uncertainty for the original request
    And it does not repeat pathname lookup and remove B
    And recovery queries or deduplicates the original operation before settling its ownership
    And it does not send a speculative extra clunk to a possibly consumed fid

  @NS_REMOVE_013 @async_adaptation
  Scenario: Cancellation before dispatch releases only temporary lookup ownership
    Given remove has acquired a temporary lookup channel but has not dispatched provider remove
    When cancellation wins before dispatch
    Then no remove request is sent
    And the temporary lookup channel is closed normally
    And the target file and unrelated descriptors remain unchanged

  @NS_REMOVE_014 @native
  Scenario: Path remove is not removal through a caller's open descriptor
    Given the same resource has two open process descriptors sharing a channel
    When remove is called on its pathname
    Then it uses its own private lookup channel
    And both descriptor slots, their shared offset and their ownership counts remain intact
    And provider policy determines whether the underlying open file remains usable
