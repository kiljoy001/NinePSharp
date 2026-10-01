@plan9_namespace @descriptor_lifetime
Feature: Local process descriptor lifetimes
  File descriptor groups are independent from namespace groups. Copies retain
  open channels, and final release closes provider state.

  @NS_FD_001 @NS_FD_002 @NS_FD_003
  Scenario Outline: Descriptor inheritance determines the effect of closing a slot
    Given a parent process with an open descriptor
    When a child receives a <namespace> namespace and a <descriptors> descriptor table
    And the parent closes its descriptor
    Then the child has <remaining> descriptors
    And the provider has been closed <closed> times
    When all descriptor owners terminate
    Then the provider has been closed 1 times
    Examples:
      | namespace | descriptors | remaining | closed |
      | Share     | Share       | 0         | 1      |
      | Copy      | Share       | 0         | 1      |
      | Empty     | Share       | 0         | 1      |
      | Share     | Copy        | 1         | 0      |
      | Copy      | Copy        | 1         | 0      |
      | Empty     | Copy        | 1         | 0      |
      | Share     | Empty       | 0         | 1      |
      | Copy      | Empty       | 0         | 1      |
      | Empty     | Empty       | 0         | 1      |

  @NS_FD_004
  Scenario: Parent exit preserves shared descriptors
    Given a parent process with an open descriptor
    When a child receives a Copy namespace and a Share descriptor table
    And the parent terminates
    Then the child has 1 descriptors
    And the provider has been closed 0 times
    When all descriptor owners terminate
    Then the provider has been closed 1 times

  @NS_FD_013
  Scenario: An admitted operation retains its channel after process exit
    Given a parent process with an open descriptor
    And an operation retaining that descriptor channel
    When all descriptor owners terminate
    Then the provider has been closed 0 times
    When the admitted operation releases its channel
    Then the provider has been closed 1 times
