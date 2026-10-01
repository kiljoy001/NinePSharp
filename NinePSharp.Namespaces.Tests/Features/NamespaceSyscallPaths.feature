Feature: Namespace syscalls resolve union paths
  Bind and mount destinations use ordinary union search while retaining the
  underlying final mount point, as walk and namec Amount do in 9front.

  Scenario Outline: Namespace control operations reach a later union member
    Given a mount point found only in the second union directory
    When I <operation> through the union path
    Then the underlying mount point is <result>
    And the parent union remains intact

    Examples:
      | operation | result   |
      | bind      | replaced |
      | mount     | replaced |
      | unmount   | unmounted |
