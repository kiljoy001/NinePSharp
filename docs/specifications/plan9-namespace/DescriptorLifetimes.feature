@plan9_namespace @descriptor_lifetime
Feature: Process descriptor and channel ownership
  Native compatibility rules follow ResourceLifetimes.md and the sibling 9front
  manuals and source. Local executable coverage and remaining integration gaps
  are recorded in Traceability.md; this file retains the full acceptance target.

  @NS_FD_001
  Scenario Outline: Descriptor inheritance is independent of namespace inheritance
    Given a parent process with fd 3 referring to an open channel
    When it creates a child with namespace mode <namespace> and descriptor mode <descriptors>
    Then the child descriptor table is <result>
    Examples:
      | namespace | descriptors | result                                      |
      | share     | share       | the same table as the parent                |
      | copy      | share       | the same table as the parent                |
      | empty     | share       | the same table as the parent                |
      | share     | copy        | independent with fd 3 on the same channel   |
      | copy      | copy        | independent with fd 3 on the same channel   |
      | empty     | copy        | independent with fd 3 on the same channel   |
      | share     | empty       | empty                                       |
      | copy      | empty       | empty                                       |
      | empty     | empty       | empty                                       |

  @NS_FD_002
  Scenario: Closing a shared slot affects every table member
    Given two processes sharing a descriptor table containing fd 3
    When one process closes fd 3
    Then fd 3 is absent for both processes
    And the slot releases one channel reference

  @NS_FD_003
  Scenario: Closing a copied slot preserves the other table's channel
    Given a parent and child with copied descriptor tables containing fd 3
    When the parent closes fd 3
    Then the child can still use fd 3 without reopening the provider
    When the child closes fd 3 and no other references remain
    Then the provider open instance is closed once

  @NS_FD_004
  Scenario: Parent exit leaves a shared descriptor group alive
    Given two processes sharing a descriptor group with two open slots
    When the parent exits
    Then the child can use both slots
    And neither slot is closed by the parent's exit
    When the child exits as the final group owner
    Then both slots are detached and their channel references released

  @NS_FD_005
  Scenario Outline: Descriptor rfork without RFPROC replaces only the current membership
    Given two processes sharing a descriptor group containing fd 3
    When one process applies <flag> without RFPROC
    Then it has <result>
    And the other process retains the original table and fd 3
    Examples:
      | flag   | result                                     |
      | RFFDG  | a copied table with fd 3 on the same channel |
      | RFCFDG | an empty descriptor table                   |

  @NS_FD_006
  Scenario: Incompatible descriptor flags fail before any ownership changes
    Given an active process with namespace and descriptor groups
    When it requests both RFFDG and RFCFDG
    Then the call fails and neither group nor any channel reference changes
    And no child is published

  @NS_FD_007
  Scenario: Dup uses the lowest free slot without reopening the channel
    Given occupied descriptor slots 0 and 2 with slot 1 free
    When the process calls dup of fd 2 with destination minus one
    Then the returned fd is 1 on the same open channel as fd 2
    And no provider open is performed

  @NS_FD_008
  Scenario Outline: Dup acquires its source before replacing the destination
    Given an open source fd 3 and an occupied destination fd <destination>
    And the source slot has OCEXEC set
    When the process duplicates fd 3 to fd <destination>
    Then the destination refers to the source channel with OCEXEC cleared
    And only the displaced slot's reference is released
    And the source channel remains open
    Examples:
      | destination |
      | 3           |
      | 4           |

  @NS_FD_009
  Scenario: An invalid dup source preserves the destination
    Given an invalid source fd and an occupied destination fd
    When the process attempts to duplicate the source to that destination
    Then duplication fails without changing the destination or closing its channel

  @NS_FD_010
  Scenario Outline: Dup destination growth follows 9front table capacity
    Given a descriptor table with capacity <capacity> and a valid source fd 0
    When fd 0 is duplicated to destination <destination>
    Then duplication is <result>
    Examples:
      | capacity | destination | result   |
      | 20       | 19          | accepted |
      | 20       | 39          | accepted |
      | 20       | 40          | rejected |
      | 5000     | 4999        | accepted |
      | 5000     | 5000        | rejected |

  @NS_FD_011
  Scenario: Copied and duplicated slots share a regular-file offset
    Given a regular-file channel shared by duplicated slots and a copied descriptor table
    When one slot reads three bytes using the implicit offset
    Then the next sequential implicit read through another slot starts at offset 3
    When another slot reads at explicit offset 0
    Then the shared regular-file offset is unchanged
    And an independent open of that file has its own offset

  @NS_FD_012
  Scenario: ORCLOSE waits for the final reference to its open instance
    Given an ORCLOSE channel retained by a parent and a copied child descriptor table
    When the parent closes its descriptor
    Then the file is not removed by that close
    When the child closes its descriptor and no other channel references remain
    Then the provider performs the remove-on-close action

  @NS_FD_013
  Scenario: Admitted I/O retains a channel after its descriptor is closed
    Given an I/O operation that has acquired a reference from fd 3
    When fd 3 is closed while that operation is pending
    Then a subsequent lookup of fd 3 fails
    And the admitted operation retains its original channel until it completes
    And final provider close waits for that reference to be released

  @NS_FD_014
  Scenario: Final close failure does not restore a native descriptor
    Given a descriptor whose final provider close will fail
    When the process closes the descriptor
    Then the descriptor remains absent
    And the provider close error is not returned as a native close syscall error

  @NS_FD_015
  Scenario: Close-on-exec is per slot and table copy preserves it
    Given a descriptor with OCEXEC set and a duplicate with OCEXEC cleared
    And the descriptor table has been copied to a child
    When the parent successfully crosses the exec boundary
    Then only its marked descriptor is closed
    And its duplicate and the child's copied slots remain usable

  @NS_FD_016
  Scenario: Exec closes marked slots in a shared table
    Given two processes sharing a table with an OCEXEC descriptor
    When one process successfully crosses the exec boundary
    Then the marked slot is absent for both processes
    And grain reactivation alone never triggers close-on-exec

  @NS_FD_017
  Scenario Outline: Mount consumes only the successful source descriptor
    Given a read-write 9P transport fd and a separate authentication fd
    When a service mount using those descriptors <outcome>
    Then the transport fd is <state>
    And the authentication fd remains allocated
    Examples:
      | outcome  | state     |
      | succeeds | consumed  |
      | fails    | allocated |
