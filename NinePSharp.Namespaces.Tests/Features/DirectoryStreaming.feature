@directory_streaming
Feature: Processes read native provider directory streams
  These executable cases cover provider offsets, union streams and mountfix buffering.
  Detailed remaining scenario mappings live in the design traceability document.

  @NS_DIR_001 @NS_DIR_002 @NS_DIR_003
  Scenario: Bounded reads through duplicated descriptors share one provider offset
    Given a native directory with records first of 64 bytes and second of 72 bytes
    When the descriptor reads at most 80 bytes
    And its duplicate reads at most 80 bytes
    Then the returned native names are first,second
    And provider offsets were 0,64

  @NS_DIR_012 @NS_DIR_013
  Scenario: Union traversal lazily opens members and retains duplicate names
    Given a native union whose members each contain the name same
    When the descriptor reads at most 100 bytes
    Then two provider handles have been opened
    When the descriptor reads at most 100 bytes
    Then the returned native names are same,same
    And three provider handles have been opened
    And one member handle has been closed

  @NS_DIR_008
  Scenario: Seek defers active member cleanup until the following read at zero
    Given a native union whose members each contain the name same
    When the descriptor reads at most 100 bytes
    And the native descriptor seeks to absolute zero
    Then no member handle has been closed
    When the descriptor reads at most 100 bytes
    Then one member handle has been closed
    And the returned native names are same,same
    And provider offsets were 0,0

  @NS_DIR_029
  Scenario: Mountfix overflow is consumed before another provider read
    Given a native directory whose 60-byte A grows to 100 bytes before 60-byte B
    When the descriptor reads at most 120 bytes
    And the descriptor reads at most 120 bytes
    Then the returned native names are A,B
    And provider offsets were 0
    And returned batch lengths were 100,60

  @NS_DIR_010
  Scenario: A zero-count native read reaches the provider
    Given a native directory with records first of 64 bytes and second of 72 bytes
    When the descriptor reads at most 0 bytes
    Then provider offsets were 0
    And returned batch lengths were 0
