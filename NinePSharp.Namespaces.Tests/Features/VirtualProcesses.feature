Feature: Virtual processes own Plan 9 process-group namespaces
  Processes normally share their namespace process group, while an RFNAMEG-style
  fork receives an independent snapshot and an RFCNAMEG-style fork receives an
  empty namespace.

  Scenario: A shared process group observes later mounts
    Given a parent virtual process
    And a child forked with a shared namespace
    When the parent mounts a replacement
    Then the child observes the replacement

  Scenario: A copied process group diverges after the fork
    Given a parent virtual process with an existing mount
    And a child forked with a copied namespace
    When the parent replaces its existing mount
    Then the child retains the original mounted resource
    And the child has a different process group

  Scenario: An empty process group inherits no mounts
    Given a parent virtual process with an existing mount
    And a child forked with an empty namespace
    Then the child has no mount at that resource

  @NS_PROC_LIFETIME
  Scenario: Terminating a parent preserves the shared child's namespace
    Given a parent virtual process with an existing mount
    And a child forked with a shared namespace
    When the parent terminates
    Then the child retains the original mounted resource
    And the shared namespace has one owner
    When the child terminates
    Then the shared namespace is closed and empty
