Feature: Orleans virtual process namespace ownership
  A virtual process keeps root and current channels while its process group
  determines whether mount-table mutations are shared, copied, or discarded.

  Scenario: A shared child observes later namespace changes
    Given an initialized Orleans virtual process
    When it forks a child sharing its process group
    And the parent mounts a resource
    Then the child observes the parent mount
    And the test cluster contains two silos

  Scenario: A copied child is isolated from later namespace changes
    Given an initialized Orleans virtual process
    And the parent has an initial mount
    When it forks a child copying its process group
    And the parent mounts a different resource
    Then the copied child retains the initial mount
    And the copied child does not observe the later mount

  Scenario: An empty child has no inherited mounts
    Given an initialized Orleans virtual process
    And the parent has an initial mount
    When it forks a child with an empty process group
    Then the child mount table is empty
    And the child retains the parent root and current directory

  Scenario: A mounted resource grain participates in namespace traversal
    Given an initialized Orleans virtual process
    And a remote resource grain is mounted on a local directory
    When a child is walked through the distributed namespace
    Then the walk reaches the remote resource grain
    And reading the remote directory returns its child

  Scenario: A process can rfork into an isolated namespace group
    Given an initialized Orleans virtual process
    And the parent has an initial mount
    When the process rforks a copied namespace with mounts disabled
    Then the process has an independent namespace group
    And the copied namespace has mounts disabled
