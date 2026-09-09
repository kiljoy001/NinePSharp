Feature: A namespace composed from mounted resources
  Plan 9 resolves mounts by object identity and treats an ordered set of mounted
  directories as a union rather than copying their contents into one directory.

  Scenario: A replacement hides the mounted-upon directory
    Given a directory named original containing old
    And a directory named replacement containing new
    When replacement is mounted over original
    Then walking new from original reaches replacement
    And walking old from original fails

  Scenario: A before union searches the new directory first
    Given a directory named original containing shared
    And a directory named replacement containing shared
    When replacement is mounted before original
    Then walking shared from original reaches replacement

  Scenario: A union falls through when its first member lacks a name
    Given a directory named original containing old
    And a directory named replacement containing new
    When replacement is mounted before original
    Then walking old from original reaches original

  Scenario: Directory reads retain union order
    Given a directory named original containing old
    And a directory named replacement containing new
    When replacement is mounted before original
    Then reading original lists new before old

  Scenario: Creation uses the first member marked for creation
    Given a directory named original containing old
    And a directory named replacement containing new
    When replacement is mounted before original and permits creation
    And a file named made is created through original
    Then replacement contains made
    And original does not contain made

  Scenario: Union mounting a regular file is rejected
    Given a regular file named original
    And a regular file named replacement
    When replacement is mounted before original
    Then the mount is rejected because unions require directories

  Scenario: Walking upward crosses back over a mount point
    Given a root containing a directory named mountpoint
    And a directory named replacement containing child
    And replacement is mounted over mountpoint
    When child is walked through mountpoint and then dot dot is walked twice
    Then the channel is back at the root

  Scenario: Binding a union copies all of its members
    Given a directory named original containing old
    And a directory named replacement containing new
    And a directory named destination containing hidden
    And replacement is mounted before original
    When the mounted original channel is bound over destination
    Then reading destination lists new before old

  Scenario: Unmount removes only the selected union member
    Given a directory named original containing old
    And a directory named replacement containing new
    And replacement is mounted before original
    When replacement is unmounted from original
    Then walking old from original reaches original
    And walking new from original fails

  Scenario: An open union channel observes a later mount
    Given a directory named original containing old
    And a directory named replacement containing new
    And replacement is mounted before original
    And a channel is open on original
    And a directory named latest containing recent
    When latest is mounted before original
    Then reading the open channel lists recent before new before old
