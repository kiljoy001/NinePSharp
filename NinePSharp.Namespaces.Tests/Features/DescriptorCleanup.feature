@plan9_namespace @descriptor_lifetime @cleanup
Feature: Local descriptor cleanup
  Cleanup detaches ownership before releasing provider state. Local close errors
  follow 9front cclose behavior; durable cleanup retries are a separate extension.

  @NS_FD_004 @NS_FD_014
  Scenario: Final-owner cleanup continues after a provider close failure
    Given a cleanup process with two descriptors
    And the first provider close fails
    When the cleanup process terminates
    Then its old descriptor table is closed and empty
    And both provider close callbacks have been attempted once in descriptor order
    And process cleanup has completed

  @NS_FD_004
  Scenario: Pending provider cleanup cannot admit new descriptors
    Given a cleanup process with two descriptors
    And the first provider close is held pending
    When process termination begins and reaches the pending close
    Then its old descriptor table is closed and empty
    And new descriptor installation is rejected without taking ownership
    And process cleanup is still pending
    When the pending provider close completes
    Then both provider close callbacks have been attempted once in descriptor order
    And process cleanup has completed

  @NS_FD_005
  Scenario: An empty descriptor rfork cleans up a table with no remaining owner
    Given a cleanup process with two descriptors
    When it applies RFCFDG as the final owner of its old descriptor table
    Then its old descriptor table is closed and empty
    And both provider close callbacks have been attempted once in descriptor order
    And the process remains active with an empty descriptor table and the same namespace

  @NS_FD_015
  Scenario: Close-on-exec releases only marked descriptors
    Given a cleanup process with two descriptors
    When its close-on-exec hook runs
    Then only the unmarked descriptor remains usable
    And only the marked descriptor provider close has been attempted
    When the cleanup process terminates
    Then both provider close callbacks have been attempted once in descriptor order
    And process cleanup has completed

  @NS_FD_014
  Scenario: A failed provider close does not restore a descriptor
    Given a cleanup process with two descriptors
    And the first provider close fails
    When the first descriptor is closed
    Then only the unmarked descriptor remains usable
    And only the marked descriptor provider close has been attempted

  @NS_FD_004
  Scenario: Repeated termination cannot repeat provider cleanup
    Given a cleanup process with two descriptors
    When the cleanup process terminates
    And termination is requested again
    Then both provider close callbacks have been attempted once in descriptor order
    And process cleanup has completed
