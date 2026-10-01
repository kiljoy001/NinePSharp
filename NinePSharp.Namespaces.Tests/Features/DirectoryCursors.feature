Feature: Metadata-backed directory syscall cursors
  Existing metadata providers supply a snapshot per enumeration.
  This adapter emits complete native stat records and shares cursor state through dup.
  Streaming provider offsets and native mountfix buffering remain outside this profile.

  Scenario: Duplicate descriptors consume one shared directory cursor
    Given a syscall directory containing file and other
    When the original descriptor reads exactly the first stat record
    And its duplicate reads the remaining stat records
    Then the directory names returned are file,other
    And the directory cursor is at EOF

  Scenario: Rewind refreshes a previously exhausted directory listing
    Given a syscall directory containing file and other
    When all its stat records have been read
    And a new entry is created and the directory is rewound
    Then a fresh directory read returns file,other,new

  Scenario: Union directory records preserve member order and duplicate names
    Given a syscall union directory with same,upper and same,lower
    When all its stat records have been read
    Then the directory names returned are same,upper,same,lower

  Scenario: An undersized buffer does not consume the next stat record
    Given a syscall directory containing file and other
    When a read buffer is too small for the first stat record
    Then a fresh directory read returns file,other
