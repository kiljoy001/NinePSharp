Feature: A Plan 9 client uses the distributed namespace dispatcher
  The Orleans implementation remains a conventional connection-oriented 9P file server.

  Scenario: Classic 9P file IO traverses a mounted resource grain
    Given a distributed 9P dispatcher and attached fid 1
    When 9P walks job to fid 2 and opens it read-write
    And 9P writes wire payload to fid 2
    Then 9P reads wire payload from fid 2
    And 9P stat reports job for fid 2
    And 9P clunk invalidates fid 2

  Scenario: Classic directory reads preserve stat record boundaries
    Given a distributed 9P dispatcher and attached fid 1
    When 9P opens fid 1 as a directory
    Then classic directory reads use complete stat records and valid offsets

  Scenario: Closing the transport clunks all open fids
    Given a distributed 9P dispatcher and attached fid 1
    And 9P walks job to fid 2 and opens it read-write
    When the 9P transport session closes
    Then the resource grain records one clunk
    And later requests on fid 2 report a bad fid

  Scenario: Flush interrupts an outstanding distributed read
    Given a distributed 9P dispatcher and attached fid 1
    And 9P walks job to fid 2 and opens it read-write
    And the next resource read is delayed
    When 9P reads fid 2 and flushes its request
    Then the read reports interruption before the resource delay ends
    And the flush succeeds

  Scenario: 9P2000.L opens and reads attributes from a mounted file
    Given a distributed 9P2000.L dispatcher and attached fid 1
    When 9P2000.L walks job to fid 2 and opens it read-write
    Then 9P2000.L getattr reports a regular file for fid 2

  Scenario: 9P2000.L directory-only open fails without consuming the fid
    Given a distributed 9P2000.L dispatcher and attached fid 1
    When 9P2000.L walks job to fid 2 and requests a directory-only open
    Then 9P2000.L reports not a directory and fid 2 can still be opened

  Scenario: 9P2000.L reads typed directory entries with continuation cookies
    Given a distributed 9P2000.L dispatcher and attached fid 1
    When 9P2000.L opens fid 1 as a directory
    Then 9P2000.L readdir reports job as a regular file

  Scenario: 9P2000.L creates and opens a file atomically
    Given a distributed 9P2000.L dispatcher and attached fid 1
    When 9P2000.L creates result on fid 1
    Then 9P2000.L returns an open result fid
    And 9P2000.L getattr reports a regular file for fid 1

  Scenario Outline: Unsupported 9P2000.L operations retain the request tag
    Given a distributed 9P2000.L dispatcher and attached fid 1
    When 9P2000.L sends unsupported <operation> with tag 20
    Then 9P2000.L returns tag 20 with operation not supported

    Examples:
      | operation  |
      | symlink    |
      | rename     |
      | readlink   |
      | xattrwalk  |
      | fsync      |
      | link       |
      | unlinkat   |

  Scenario: 9P2000.L version negotiation retains the dialect
    Given a distributed 9P2000.L dispatcher and attached fid 1
    When the client negotiates 9P2000.L
    Then the negotiated version is 9P2000.L

  Scenario Outline: A request that ignores cancellation is abandoned with an unknown outcome
    Given a distributed 9P dispatcher with a drain limit of 200 milliseconds whose attach resolver never answers
    And an attach is in flight
    When <drain>
    Then it finishes within 2 seconds
    And the attach was answered before it finished
    And the attach is answered with the error "unknown"
    And the attach's outcome is logged as unknown

    Examples:
      | drain                                |
      | the transport closes during the attach |
      | 9P negotiates the version during the attach |
      | 9P flushes the attach                |

  Scenario: A flushed tag can be used again while its abandoned request still runs
    Given a distributed 9P dispatcher with a drain limit of 200 milliseconds whose attach resolver never answers
    And an attach is in flight
    When 9P flushes the attach
    And 9P attaches again with the same tag
    Then the second attach is not refused as a duplicate tag
