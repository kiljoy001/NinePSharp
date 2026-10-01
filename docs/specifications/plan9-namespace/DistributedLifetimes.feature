@plan9_namespace @distributed_extension @durable_lifetime
Feature: Recoverable process and resource ownership
  These are NinePSharp recovery extensions to the native ownership rules.
  They are specified, pending persistent Orleans and gateway test bindings.

  @NS_LIFE_001
  Scenario: Reactivation preserves ownership rather than simulating exit
    Given a persisted active process with namespace and descriptor memberships
    When its process and group grains deactivate and reactivate
    Then the memberships and open-instance identities are unchanged
    And no provider close is caused by deactivation

  @NS_LIFE_002
  Scenario: Replayed ownership messages cannot release a replacement slot
    Given a released ownership token for fd 3 and a new allocation at fd 3
    When the old release message is delivered again
    Then the new allocation retains its channel reference
    And the old release has no additional effect

  @NS_LIFE_003
  Scenario Outline: Fork recovers every ownership transition
    Given a parent starting a fork with copied namespace and descriptor groups
    When the coordinator fails <boundary>
    And persistent grains recover with all providers available
    Then the journal resolves to one runnable child or a recorded abort with no child
    And a committed child owns both groups before admitting application calls
    And an abort releases every prepared reference
    And replaying the original operation cannot allocate another child or group
    Examples:
      | boundary                              |
      | after intent persistence              |
      | after destination reservation         |
      | after channel references are acquired |
      | after the commit decision             |
      | after child state persistence         |
      | after activation before the reply     |

  @NS_LIFE_004
  Scenario: Group replacement recovers without losing old or new ownership
    Given a process replacing both groups without creating a child
    When it fails after persisting new group pointers but before releasing old membership
    And the process reactivates
    Then recovery completes the recorded transfer
    And the process has usable destination memberships
    And old memberships and preparation references are released once

  @NS_LIFE_005
  Scenario: Unknown transfer outcome retains prepared references
    Given a prepared group transfer whose coordinator is unreachable
    When its recovery timer expires without a durable decision
    Then the transfer reports recovery pending
    And it neither releases prepared references nor publishes a runnable child

  @NS_LIFE_006
  Scenario: Repeated group replacements use distinct identities
    Given a process that has completed a namespace-copy replacement
    When it starts a new namespace-copy replacement with a different operation ID
    Then it receives a fresh group identity
    And retrying either operation returns that operation's original identity

  @NS_LIFE_007
  Scenario: Termination races with a prepared fork
    Given a prepared child and an exit request for its parent
    When recovery resolves the fork decision
    Then a committed child remains runnable with its own memberships after parent exit
    And an aborted child is never runnable and retains no preparation references

  @NS_LIFE_008
  Scenario: Termination fences new work and accounts for a late open
    Given an open request durably admitted before the process termination cutoff
    When termination is persisted before the provider returns its handle
    Then new calls for that process incarnation fail admission
    And the late handle is recorded for cleanup without publishing a descriptor or fid
    And a lost open reply is recovered with the original operation ID

  @NS_LIFE_009
  Scenario: Parent termination cannot revoke a surviving child's shared handle
    Given a child retaining a channel originally opened by its parent
    When the parent terminates
    Then the child can use the channel under its own current ownership
    And cleanup of the parent does not release the child's reference

  @NS_LIFE_010
  Scenario: Cleanup survives provider outage and lost close acknowledgement
    Given a terminating process whose final close intent is durable
    When the provider closes the handle but its acknowledgement is lost
    And the provider becomes unreachable and both grains reactivate
    Then status reports provider cleanup pending
    When the provider becomes reachable
    Then recovery retries the same close operation ID
    And provider deduplication prevents a second close effect
    And the acknowledged cleanup disappears from the pending count

  @NS_LIFE_011
  Scenario: Terminated identities and closed groups cannot be resurrected
    Given a terminated process incarnation and its closed final-owner groups
    When delayed initialize, acquire, or mutation messages address those identities
    Then they fail without reopening either group
    And reusing a numeric PID with a new incarnation grants the old messages no authority

  @NS_LIFE_012
  Scenario: Disconnect releases fids while preserving application descriptors
    Given a durable application process with an open descriptor
    And a process-bound connection with a separate open fid
    When that connection disconnects
    Then its fid reference is released
    And the application descriptor and process remain active
    And a fresh connection inherits none of the old fids

  @NS_LIFE_013
  Scenario: Process exit closes its bound sessions but preserves an administrator
    Given two sessions bound to one process incarnation
    And a separately authenticated administrative session controlling that process
    When the process terminates
    Then both bound sessions reject new work and release their fid references
    And the administrative session remains usable

  @NS_LIFE_014
  Scenario: Reused operation IDs cannot change the request
    Given a committed lifecycle operation with a recorded request fingerprint
    When the same operation ID is submitted with different arguments
    Then the request fails without changing ownership

  @NS_LIFE_015
  Scenario: Recovery does not reopen a non-recoverable handle by pathname
    Given a provider handle declared non-recoverable across provider loss
    When its provider loses the open state
    Then the old handle reports handle lost
    And recovery never substitutes a new open at the same pathname

  @NS_LIFE_016
  Scenario: Legacy group ownership is migrated before final-owner cleanup
    Given legacy persisted processes and groups with no owner registry
    When lifecycle migration cannot enumerate every process record
    Then migration fails without deleting apparently unowned groups
    And lifecycle mutations remain disabled until a complete migration commits

  @NS_LIFE_017
  Scenario: Remote membership admission cannot race final group closure
    Given a group with one owner and an uncommitted membership acquisition
    When final-owner release and acquisition contend in the group grain
    Then acquisition either reserves ownership before closure or fails as closed
    And no committed process refers to a closed group

  @NS_LIFE_018
  Scenario: Definitive provider cleanup failure is visible without restoring ownership
    Given a detached final reference with a durable cleanup intent
    When the provider reports a definitive close failure
    Then cleanup status retains a terminal failure for reconciliation
    And no fd, fid, or process membership is restored
