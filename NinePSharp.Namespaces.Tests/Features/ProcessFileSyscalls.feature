Feature: Native regular file syscalls
  Processes use retained channels for regular-file I/O and share positions through dup.

  @NS_IO_003
  Scenario: Native create truncates an existing file and preserves its identity
    Given a syscall process with an open file containing abcdef
    When native create opens that existing file again
    Then the newly created descriptor has length zero and the same resource identity

  @NS_IO_004
  Scenario: Exclusive create preserves an existing file
    Given a syscall process with an open file containing abcdef
    When exclusive create attempts that existing name
    Then creation fails and the original contents remain abcdef

  @NS_IO_005
  Scenario: A full descriptor table cleans up a successfully created file handle
    Given a syscall process with an open file containing abcdef
    And its descriptor table is full
    When native create attempts a new name
    Then descriptor allocation fails and the created handle is closed once
    And the new file still exists

  @NS_IO_006
  Scenario: Duplicated descriptors share the position after short reads
    Given a syscall process with an open file containing abcdef
    When it reads two bytes through a duplicate descriptor
    Then the syscall read returns ab
    And the original descriptor position is 2

  @NS_IO_007
  Scenario: Positioned reads leave the shared position unchanged
    Given a syscall process with an open file containing abcdef
    When it reads two bytes explicitly at offset three
    Then the syscall read returns de
    And the original descriptor position is 0

  @NS_IO_008
  Scenario: Failed writes release their position reservation
    Given a syscall process with an open file containing abcdef
    When an eight byte implicit write fails at the provider
    Then the original descriptor position is 0
    And the syscall file remains open

  @NS_IO_012
  Scenario: Exit rejects late open publication into a surviving shared table
    Given a syscall process with an open file containing abcdef
    And a child sharing its descriptors
    When its second open finishes after its exit
    Then that open fails and its provider handle is closed once
    And the child still has the original usable descriptor
