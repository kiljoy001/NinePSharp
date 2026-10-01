@plan9_namespace @process_groups
Feature: Process namespace groups
  A process has root and current channels. Its process group owns the mount
  table, and rfork-style operations determine whether a child shares, copies,
  or receives an empty namespace.

  @NS_PROC_001
  Scenario: A shared child observes a later parent mount
    Given an initialized virtual process
    When it forks a child sharing its namespace group
    And the parent mounts a resource
    Then the child observes the new mount

  @NS_PROC_002
  Scenario: A copied child is isolated from later parent mounts
    Given an initialized virtual process with an initial mount
    When it forks a child copying its namespace group
    And the parent mounts a different resource
    Then the child retains the initial mount
    And the child does not observe the later mount

  @NS_PROC_003
  Scenario: An empty child receives no inherited mounts
    Given an initialized virtual process with an initial mount
    When it forks a child with an empty namespace group
    Then the child namespace has no mounts
    And the child retains the parent's root channel
    And the child retains the parent's current channel

  @NS_PROC_004
  Scenario: Changing directory replaces only the current channel
    Given an initialized virtual process with root and current channels
    When it changes directory to a reachable directory
    Then its current channel is the requested directory
    And its root channel is unchanged

  @NS_PROC_005
  Scenario: The final process owner closes its namespace group
    Given a virtual process that is the final owner of its namespace group
    When the process terminates
    Then its root and current channels are released
    And the namespace group has no mounted resources
    When a retained reference attempts a mount or walk in that namespace
    Then the operation fails with a closed-namespace error

  @NS_PROC_011
  Scenario: Terminating a parent preserves a shared child's namespace
    Given a virtual process with a mounted resource
    And a child sharing its namespace group
    When the parent terminates
    Then the child retains the mounted resource
    And the namespace group remains open until its final owner terminates

  @NS_PROC_006
  Scenario: Mount permission is enforced by the process namespace
    Given a process group that is denied access to a mount-capable device
    When a process in that group attempts to mount the device
    Then the mount is rejected before the provider is contacted

  @NS_PROC_007
  Scenario: RFNOMNT prevents inherited mount operations
    Given a process created with mount operations disabled
    When it attempts to bind or mount a resource
    Then the operation is rejected without changing its namespace

  @NS_PROC_008
  Scenario: Namespace cloning preserves relative mount order
    Given a process group with multiple ordered union members
    When its namespace group is copied
    Then the copy contains the same mount members in the same order
    And later mutations of either group are independent

  @NS_PROC_009
  Scenario: A process keeps its namespace while its grain is reactivated
    Given a persisted virtual process and namespace group
    When both grains deactivate and reactivate
    Then root, current directory, and mount state are restored
    And resource identities remain stable

  @NS_PROC_010
  Scenario: Namespace rfork flags can modify the current process group
    Given a process with a populated namespace group
    When it applies the namespace-copy flag without creating a child
    Then the process receives an independent namespace group
    And later mounts by the original group are not visible to it
