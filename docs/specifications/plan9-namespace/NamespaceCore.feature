@plan9_namespace @namespace_core
Feature: Plan 9 namespace traversal and mounts
  A namespace resolves paths through channels, mount heads, and ordered mount
  members. An open channel retains its resource identity while observing later
  changes to the namespace group.

  @NS_CORE_001
  Scenario: A replacement mount hides the mounted-upon directory
    Given a directory channel named original containing old
    And a directory channel named replacement containing new
    When replacement is mounted over original
    Then walking new from original reaches replacement
    And walking old from original fails

  @NS_CORE_002
  Scenario: A before union searches members in order
    Given a directory channel named original containing shared
    And a directory channel named replacement containing shared
    When replacement is mounted before original
    Then walking shared from original reaches replacement
    And reading original lists replacement entries before original entries

  @NS_CORE_003
  Scenario: A union falls through when an earlier member lacks a name
    Given a directory channel named original containing old
    And a directory channel named replacement containing new
    When replacement is mounted before original
    Then walking old from original reaches original

  @NS_CORE_004
  Scenario: Creation uses the first creatable union member
    Given a directory channel named original containing old
    And a directory channel named replacement containing new
    When replacement is mounted before original with create permission
    And a file named made is created through original
    Then replacement contains made
    And original does not contain made

  @NS_CORE_005
  Scenario: A union mount of regular files is rejected
    Given a regular file channel named original
    And a regular file channel named replacement
    When replacement is mounted before original
    Then the mount fails with a directory-required error

  @NS_CORE_006
  Scenario: Dot dot crosses back over a mount point
    Given a root channel containing a directory named mountpoint
    And a directory channel named replacement containing child
    And replacement is mounted over mountpoint
    When child is walked through mountpoint and dot dot is walked twice
    Then the resulting channel is the root channel
    And its visible path is empty

  @NS_CORE_007
  Scenario: An open union channel observes a later mount
    Given a directory channel named original containing old
    And a directory channel named replacement containing new
    And replacement is mounted before original
    And a channel is open on original
    When a directory channel named latest containing recent is mounted before original
    Then reading the open channel lists recent before new before old

  @NS_CORE_008
  Scenario: Selected unmount removes one union member
    Given a directory channel named original containing old
    And a directory channel named replacement containing new
    And replacement is mounted before original
    When replacement is unmounted from original
    Then walking old from original reaches original
    And walking new from original fails

  @NS_CORE_009
  Scenario: Unmount without a selected member removes the complete mount
    Given a directory channel named original containing old
    And a directory channel named replacement containing new
    And replacement is mounted over original
    When every member is unmounted from original
    Then walking old from original reaches original
    And walking new from original fails

  @NS_CORE_010
  Scenario: Binding a union copies all members in their existing order
    Given a directory channel named original containing old
    And a directory channel named replacement containing new
    And a directory channel named destination containing hidden
    And replacement is mounted before original
    When the mounted original channel is bound over destination
    Then reading destination lists new before old

  @NS_CORE_011
  Scenario: Namespace lookup uses channel identity rather than pathname text
    Given two distinct directory channels with the same displayed path
    When one channel is mounted over a target channel
    Then walking through the target reaches only the mounted channel
    And mounting through the other same-named channel does not alter the target

  @NS_CORE_012
  Scenario: A failed walk does not partially update the caller channel
    Given a channel positioned at a directory containing child
    When a walk contains child followed by a missing name
    Then the walk reports the successful prefix
    And the caller channel remains at its original position

  @NS_CORE_013
  Scenario: A creatable bind cannot copy an incompatible source union
    Given a source channel representing a union with multiple members
    When the source is bound with create permission
    Then the bind fails with a create-bind error
    And the target namespace is unchanged

  @NS_CORE_014
  Scenario: Union directory reads preserve duplicate names
    Given two union members that each contain a child named shared
    When the second member is mounted after the first
    Then reading the union returns both shared entries in member order

  @NS_CORE_015
  Scenario: Creation fails when no union member permits creation
    Given a union directory whose members do not permit creation
    When a file is created through the union
    Then creation fails with a create-not-permitted error
