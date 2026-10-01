@plan9_namespace @file_stat
Feature: Processes inspect native file metadata

  @NS_META_001 @NS_META_040
  Scenario: Root stat crosses the current mount without opening a descriptor
    Given a stat process with a new resource mounted on its root
    When it stats the process root
    Then root metadata comes from the mounted resource with an empty visible name
    And stat allocates no descriptor

  @NS_META_003 @NS_META_009
  Scenario: Fstat retains its mounted resource and visible alias across unmount
    Given a stat process with other bound onto file and file opened
    When the file binding is removed
    And it stats the retained file descriptor
    Then metadata comes from other with visible name file
    And fresh path stat selects the original file

  @NS_META_012 @NS_META_013
  Scenario: A longer visible name needs separate provider and rewritten size hints
    Given a stat descriptor with provider size 80 and a visible name twelve bytes longer
    When it requests stat capacities 2 then 80 then 92
    Then stat returns lengths 2 then 2 then 92 with size hints 78 then 90 then 90

  @NS_META_004 @NS_META_007
  Scenario: Fstat accepts write-only descriptors without changing the shared position
    Given a write-only stat descriptor positioned at 73
    When it stats the retained file descriptor
    Then the visible stat name is file and the shared position is still 73
