@plan9_namespace @namespace_devices
Feature: Plan 9 namespace service projections
  Conventional Plan 9 directories are resource providers. Their implementation
  may be local or Orleans-backed, but their observable 9P behavior is stable.

  @NS_DEV_001
  Scenario: A process namespace is inspectable through proc
    Given an initialized virtual process with a current directory and mounts
    When a client reads /proc/<pid>/ns
    Then the response describes the current directory
    And it lists mounts in mount allocation order
    And the description can reconstruct an equivalent namespace

  @NS_DEV_002 @extension
  Scenario: A service is published through srv
    Given a running resource grain
    When it publishes a named service through /srv
    Then a client can discover the service entry
    And opening the entry returns a mountable service handle

  @NS_DEV_003
  Scenario: A shared mount entry is visible across process groups
    Given a service published in the shared mount registry
    When two independent process groups inspect /shr
    Then both observe the same service entry
    And each group may mount it into its own namespace

  @NS_DEV_004
  Scenario: Removing a shared mount entry does not invalidate open channels
    Given a client with an open channel to a /shr service entry
    When the service entry is removed from /shr
    Then new opens fail
    And the existing channel remains valid until it is clunked or revoked

  @NS_DEV_005
  Scenario: A remote provider participates in ordinary namespace traversal
    Given a resource grain mounted below /apps
    When a 9P client walks and reads /apps/resource
    Then the client observes ordinary Qids, directory entries, and file data
    And it does not need a provider-specific protocol
