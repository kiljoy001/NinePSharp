Feature: Plan 9 compatible namespace fid sessions
  Fids belong to one connection and retain object-based namespace channels.

  Scenario: Two connections may use the same fid independently
    Given two namespace sessions attached with fid 1
    When the first session walks fid 1 to child as fid 2
    Then only the first session contains fid 2

  Scenario: A partial walk does not allocate the new fid
    Given a namespace session attached with fid 1
    When fid 1 partially walks child then missing as fid 2
    Then the walk returns one qid
    And fid 2 is not allocated
    And fid 1 still selects the root

  Scenario: An open fid cannot be walked
    Given a namespace session attached with fid 1
    And fid 1 is opened for reading
    When the client attempts to walk the open fid
    Then the walk is rejected as an open fid

  Scenario: Create replaces the directory fid with an open child
    Given a namespace session attached with fid 1
    When the client creates result with fid 1
    And writes payload through fid 1
    Then fid 1 reads payload from result
    And stat reports the visible name result

  Scenario: Clunk invalidates a fid even when provider cleanup fails
    Given a namespace session attached with fid 1
    And fid 1 is opened for reading
    And provider clunk will fail
    When the client clunks fid 1
    Then the clunk failure is reported
    And fid 1 is no longer allocated

  Scenario: Closing a connection clunks its open fids
    Given a namespace session attached with fid 1
    And fid 1 is opened for reading
    When the namespace session is closed
    Then its provider open handle is clunked
    And fid 1 is no longer allocated
