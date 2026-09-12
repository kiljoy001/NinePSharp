@plan9_namespace @control_plane @extension
Feature: Remote namespace control through 9P
  Namespace syscalls are local kernel operations on Plan 9. A distributed
  implementation exposes equivalent operations through a 9P control tree.

  @NS_CTL_001
  Scenario: A client submits a bind request through ctl
    Given an attached client with a selected process namespace
    When it writes a bind request naming a source, target, and before flag
    Then the server assigns an operation identifier
    And the operation appears as pending in status
    And the namespace is unchanged until the operation is accepted

  @NS_CTL_002
  Scenario: A completed namespace request is observable through reply
    Given a pending namespace operation
    When the operation completes successfully
    Then status reports success and the operation identifier
    And reply contains the resulting namespace version
    And a subsequent walk observes the requested mount

  @NS_CTL_003
  Scenario: Invalid namespace requests return Plan 9 style errors
    Given an attached client with a selected process namespace
    When it submits a request with an invalid flag combination or missing target
    Then the request fails with a stable error code and message
    And no namespace mutation occurs

  @NS_CTL_004
  Scenario: Repeating an operation identifier is idempotent
    Given a successfully completed namespace operation
    When the client submits the same operation identifier again
    Then the server returns the original result
    And the mount is not applied a second time

  @NS_CTL_005
  Scenario: Concurrent namespace changes use an explicit version
    Given two clients operating on the same process namespace
    When both submit mutations against the same namespace version
    Then exactly one mutation is accepted
    And the other receives a version-conflict error
    And the accepted mutation is visible atomically

  @NS_CTL_006
  Scenario: A client cannot select another principal's namespace
    Given two authenticated principals with separate namespace groups
    When one client names the other principal's process or group identifier
    Then the server denies the request
    And the client's selected namespace remains unchanged

  @NS_CTL_007
  Scenario: Long namespace work completes asynchronously
    Given a namespace operation that requires a remote provider
    When the client writes the request
    Then the write returns without blocking on provider completion
    And the client can poll status or read a completion notification

  @NS_CTL_008
  Scenario: Disconnecting a client does not corrupt an accepted operation
    Given an accepted namespace operation with an operation identifier
    When the submitting client disconnects
    Then the operation either completes or reaches a recorded failure
    And a later client can retrieve its final status
