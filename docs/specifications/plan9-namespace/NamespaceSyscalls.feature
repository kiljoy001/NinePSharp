@plan9_namespace @namespace_syscalls
Feature: Plan 9 namespace syscall contract
  The syscall layer evaluates names in the caller's namespace, validates mount
  flags and channel modes, and applies changes to the caller's process group.

  @NS_SYS_001
  Scenario: Bind evaluates the source at bind time
    Given a source path that currently resolves to resource first
    And a target path in the caller's namespace
    When the source is bound over the target
    And the source path is later rebound to resource second
    Then the target continues to resolve to resource first

  @NS_SYS_002
  Scenario: Bind and mount require matching object kinds
    Given a directory target and a regular-file source
    When the client attempts to bind the source over the target
    Then the syscall fails with a type-mismatch error
    And the target namespace is unchanged

  @NS_SYS_003
  Scenario: Mount requires a read-write 9P service channel
    Given a service descriptor opened read-only
    When the client attempts to mount that descriptor
    Then the syscall fails before changing the namespace
    When a read-write 9P service descriptor is mounted
    Then the service tree is visible at the target

  @NS_SYS_004
  Scenario: Mount selects the requested server tree
    Given a read-write 9P connection serving multiple named trees
    When the client mounts the connection with an explicit attach name
    Then the target exposes the selected tree
    And an empty attach name selects the server default tree

  @NS_SYS_005
  Scenario: A successful mount consumes its service descriptor
    Given a read-write service descriptor ready to mount
    When the mount succeeds
    Then the namespace owns the mounted service reference
    And the caller's service descriptor is closed

  @NS_SYS_006
  Scenario: Authentication is optional but explicit
    Given a mount request without an authentication descriptor
    When the service does not require authentication
    Then the mount succeeds with no authentication channel
    When the service requires authentication and no valid descriptor is supplied
    Then the mount fails without changing the namespace

  @NS_SYS_007
  Scenario: Bind and mount affect every process in the namespace group
    Given two processes sharing one namespace group
    When one process binds a resource over a target
    Then the other process observes the binding
    And a process in a copied namespace group does not observe it

  @NS_SYS_008
  Scenario: Unmount without a source removes every member at the target
    Given a target with several bound or mounted members
    When the client unmounts the target without selecting a source
    Then every member is removed
    And the original mounted-upon resource is visible again

  @NS_SYS_009
  Scenario: Before and after flags cannot be combined
    Given a source and target with compatible object kinds
    When the client requests a mount with both before and after flags
    Then the syscall fails with an invalid-flags error
    And no provider is contacted

  @NS_SYS_010
  Scenario: Cache is restricted to service mounts
    Given a local source channel and a target directory
    When the client requests a bind with the cache flag
    Then the syscall fails with an invalid-flags error
    When the client requests a service mount with the cache flag
    Then the mount is accepted if the service permits caching

  @NS_SYS_011
  Scenario: A service mount requires a directory target
    Given a read-write 9P service descriptor and a regular-file target
    When the client attempts to mount the service on the target
    Then the syscall fails with a directory-required error
    And the service descriptor remains available to the caller
