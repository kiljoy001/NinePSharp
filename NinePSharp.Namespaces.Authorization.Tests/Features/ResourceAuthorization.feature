@namespace_authorization
Feature: Resource operations in a principal's view are authorized
  The layer decorates the provider used by the namespace data plane. Every scenario
  records provider calls so that a denial can be shown to happen before dispatch.
  Mode, membership, open-bit, walk, create and remove rules follow 9front gefs
  (sys/src/cmd/gefs/fs.c); the remove-on-close parent check follows hjfs fs2.c.

  Background:
    Given the resource tree
      | path          | kind      | owner  | group   | mode |
      | /             | directory | glenda | sys     | 0755 |
      | /data         | directory | glenda | writers | 0775 |
      | /data/report  | file      | glenda | writers | 0664 |
      | /data/private | file      | glenda | writers | 0600 |
      | /data/tools   | file      | glenda | writers | 0751 |
      | /archive      | directory | glenda | sys     | 0755 |
      | /archive/old  | file      | glenda | sys     | 0666 |
    And the principals
      | user    | enabled |
      | glenda  | true    |
      | alice   | true    |
      | bob     | true    |
      | writers | true    |
      | carol   | true    |
      | none    | true    |
      | mallory | false   |
    And group "writers" has members "alice"
    And the policy generation is 1

  @NS_AUTHZ_001
  Scenario: A read allowed by every layer succeeds
    Given the grants
      | kind | subject | path         | scope | rights          |
      | user | alice   | /data/report | self  | stat,walk,read  |
    When alice opens "/data/report" for OREAD
    Then the open succeeds

  @NS_AUTHZ_001
  Scenario Outline: An open denied by any single layer fails before dispatch
    Given the grants
      | kind | subject   | path         | scope | rights               |
      | user | <grantee> | /data/report | self  | stat,walk,read,write |
    And <condition>
    When <user> opens "/data/report" for <open>
    Then the open <outcome>

    Examples:
      | user    | grantee | condition                        | open   | outcome                            |
      | mallory | mallory | nothing else changes             | OREAD  | is denied before provider dispatch |
      | alice   | alice   | the policy generation becomes 2  | OREAD  | is denied before provider dispatch |
      | alice   | bob     | nothing else changes             | OREAD  | reports not found                  |
      | alice   | alice   | "/data/report" has mode 0600     | OREAD  | is denied before provider dispatch |
      | alice   | alice   | "/data" is mounted read-only     | OWRITE | is denied before provider dispatch |

  @NS_AUTHZ_002
  Scenario Outline: Owner, group and other permission sets follow intro(5)
    Given the grants
      | kind | subject | path         | scope | rights    |
      | user | <user>  | /data/report | self  | stat,read |
    And "/data/report" has mode <mode>
    When <user> opens "/data/report" for OREAD
    Then the open <outcome>

    Examples:
      | user   | mode | outcome                                  |
      | glenda | 0040 | is denied before provider dispatch       |
      | glenda | 0060 | is denied before provider dispatch       |
      | glenda | 0004 | succeeds                                 |
      | alice  | 0400 | is denied before provider dispatch       |
      | alice  | 0040 | succeeds                                 |
      | alice  | 0004 | succeeds                                 |
      | bob    | 0040 | is denied before provider dispatch       |
      | bob    | 0004 | succeeds                                 |

  @NS_AUTHZ_002
  Scenario Outline: The user none gets only the other set, even as owner
    Given the grants
      | kind | subject | path         | scope | rights    |
      | user | none    | /data/report | self  | stat,read |
    And "/data/report" is owned by "none"
    And "/data/report" has mode <mode>
    When none opens "/data/report" for OREAD
    Then the open <outcome>

    Examples:
      | mode | outcome                            |
      | 0400 | is denied before provider dispatch |
      | 0004 | succeeds                           |

  @NS_AUTHZ_002
  Scenario: Members of nogroup lose the other set except to search directories
    Given group "nogroup" has members "carol"
    And the grants
      | kind | subject | path         | scope | rights    |
      | user | carol   | /            | self  | walk      |
      | user | carol   | /data        | self  | stat,walk |
      | user | carol   | /data/report | self  | stat,read |
    And "/data" has mode 0001
    And "/data/report" has mode 0004
    When carol walks to "/data/report"
    Then the walk succeeds
    When carol opens "/data/report" for OREAD
    Then the request is denied before provider dispatch

  @NS_AUTHZ_003
  Scenario: A user is a member of its same-name group and of explicit groups
    Given the grants
      | kind  | subject | path          | scope | rights    |
      | group | writers | /data/private | self  | stat,read |
    And "/data/private" has mode 0040
    When writers opens "/data/private" for OREAD
    Then the open succeeds
    When alice opens "/data/private" for OREAD
    Then the open succeeds
    When bob opens "/data/private" for OREAD
    Then the request reports not found

  @NS_AUTHZ_003
  Scenario: Group membership is not inherited through nested groups
    Given group "staff" has members "writers"
    And the grants
      | kind  | subject | path         | scope | rights    |
      | group | staff   | /data/report | self  | stat,read |
    When alice opens "/data/report" for OREAD
    Then the request reports not found

  @NS_AUTHZ_004
  Scenario: Each directory left needs the walk right and execute permission
    Given the grants
      | kind | subject | path         | scope | rights         |
      | user | bob     | /            | self  | walk           |
      | user | bob     | /data        | self  | stat           |
      | user | bob     | /data/report | self  | stat,read      |
    When bob walks to "/data/report"
    Then the walk is denied at "/data"
    And no provider walk was made from "/data"

  @NS_AUTHZ_004
  Scenario: Search permission is the execute bit, not the read bit
    Given the grants
      | kind | subject | path  | scope | rights              |
      | user | bob     | /     | tree  | stat,walk,read      |
    And "/data" has mode 0774
    When bob walks to "/data/report"
    Then the walk is denied at "/data"

  @NS_AUTHZ_005
  Scenario: A hidden object looks absent
    Given the grants
      | kind | subject | path         | scope | rights         |
      | user | alice   | /            | self  | stat,walk,read |
      | user | alice   | /data        | self  | stat,walk,read |
      | user | alice   | /data/report | self  | stat,read      |
    When alice walks to "/data/private"
    Then the walk reports not found
    When alice walks to "/data/missing"
    Then the walk reports not found
    When alice lists "/data"
    Then the listing contains exactly "report"
    When alice stats the handle of "/data/private" obtained elsewhere
    Then the request reports not found

  @NS_AUTHZ_006
  Scenario: A hidden first union member does not shadow a later one
    Given a union at "/data" of "/archive" before "/data"
    And the grants
      | kind | subject | path          | scope | rights         |
      | user | alice   | /             | self  | stat,walk      |
      | user | alice   | /data         | tree  | stat,walk,read |
    When alice walks to "/data/old"
    Then the walk reports not found
    When alice lists the union at "/data"
    Then the listing contains exactly "private, report, tools"

  @NS_AUTHZ_006
  Scenario: A union member whose directory open is denied is skipped by definite rejection
    Given the grants
      | kind | subject | path  | scope | rights    |
      | user | alice   | /data | self  | stat,walk |
    When alice opens "/data" for OREAD
    Then the request is a definite directory rejection

  @NS_AUTHZ_007
  Scenario Outline: Each open mode needs its rights and permission
    Given the grants
      | kind | subject | path  | scope | rights   |
      | user | alice   | /data | tree  | <rights> |
    And "<path>" has mode <mode>
    When alice opens "<path>" for <open>
    Then the open <outcome>

    Examples:
      | path          | open          | rights                  | mode | outcome                            |
      | /data/report  | OREAD         | stat,read               | 0664 | succeeds                           |
      | /data/report  | OREAD         | stat,write              | 0664 | is denied before provider dispatch |
      | /data/report  | OWRITE        | stat,write              | 0664 | succeeds                           |
      | /data/report  | OWRITE        | stat,write              | 0644 | is denied before provider dispatch |
      | /data/report  | ORDWR         | stat,read,write         | 0664 | succeeds                           |
      | /data/report  | ORDWR         | stat,read               | 0664 | is denied before provider dispatch |
      | /data/report  | ORDWR         | stat,read,write         | 0644 | is denied before provider dispatch |
      | /data/tools   | OEXEC         | stat,read               | 0751 | succeeds                           |
      | /data/tools   | OEXEC         | stat,read               | 0761 | is denied before provider dispatch |
      | /data/tools   | OEXEC         | stat,read               | 0711 | is denied before provider dispatch |
      | /data/report  | OREAD,OTRUNC  | stat,read               | 0664 | is denied before provider dispatch |
      | /data/report  | OREAD,OTRUNC  | stat,read,write         | 0664 | succeeds                           |
      | /data/report  | OREAD,ORCLOSE | stat,read               | 0664 | is denied before provider dispatch |
      | /data/report  | OREAD,ORCLOSE | stat,read,remove        | 0664 | succeeds                           |

  @NS_AUTHZ_007
  Scenario: Remove-on-close needs write permission in the parent
    Given the grants
      | kind | subject | path  | scope | rights                |
      | user | alice   | /data | tree  | stat,read,remove      |
    And "/data" has mode 0755
    When alice opens "/data/report" for OREAD,ORCLOSE
    Then the request is denied before provider dispatch

  @NS_AUTHZ_008
  Scenario: A read handle cannot write and foreign handles are rejected
    Given the grants
      | kind | subject | path  | scope | rights                |
      | user | alice   | /data | tree  | stat,read,write       |
    And alice has opened "/data/report" for OREAD
    When alice writes "x" through that handle
    Then the request is denied before provider dispatch
    When alice reads through a handle opened directly from the provider
    Then the request is denied before provider dispatch

  @NS_AUTHZ_009
  Scenario: A mode change keeps an open handle but denies a new open
    Given the grants
      | kind | subject | path  | scope | rights    |
      | user | alice   | /data | tree  | stat,read |
    And alice has opened "/data/report" for OREAD
    When "/data/report" has mode 0600
    Then alice can read through that handle
    When alice opens "/data/report" for OREAD
    Then the request is denied before provider dispatch

  @NS_AUTHZ_010
  Scenario: A new policy generation revokes retained handles
    Given the grants
      | kind | subject | path  | scope | rights          |
      | user | alice   | /data | tree  | stat,read,write |
    And alice has opened "/data/report" for ORDWR
    When the policy generation becomes 2
    Then reading through that handle is denied before provider dispatch
    And writing through that handle is denied before provider dispatch
    And clunking that handle reaches the provider

  @NS_AUTHZ_011
  Scenario Outline: Create masks the child's permissions as create(5) specifies
    Given the grants
      | kind | subject | path  | scope | rights                       |
      | user | alice   | /data | tree  | stat,walk,read,write,create  |
    And "/data" has mode <dirmode>
    When alice creates <kind> "new" in "/data" with permissions <perm> for <open>
    Then the provider receives permissions <expected>

    Examples:
      | kind      | dirmode | perm | open   | expected |
      | file      | 0775    | 0777 | OWRITE | 0775     |
      | file      | 0770    | 0666 | OWRITE | 0660     |
      | file      | 0770    | 0751 | OWRITE | 0751     |
      | directory | 0775    | 0777 | OREAD  | 0775     |
      | directory | 0770    | 0777 | OREAD  | 0770     |

  @NS_AUTHZ_011
  Scenario Outline: A create denied by the parent or the inherited rights is a definite rejection
    Given the grants
      | kind | subject | path  | scope | rights   |
      | user | alice   | /data | tree  | <rights> |
    And "/data" has mode <dirmode>
    When alice creates file "new" in "/data" with permissions 0664 for <open>
    Then the request is a definite create rejection with no provider call

    Examples:
      | rights               | dirmode | open   |
      | stat,walk,write      | 0775    | OWRITE |
      | stat,walk,create     | 0775    | OWRITE |
      | stat,walk,write,create | 0755    | OWRITE |

  @NS_AUTHZ_012
  Scenario Outline: Remove needs the remove right and write permission in the parent
    Given the grants
      | kind | subject | path  | scope | rights   |
      | user | alice   | /data | tree  | <rights> |
    And "/data" has mode <dirmode>
    When alice removes "/data/report"
    Then the remove <outcome>

    Examples:
      | rights           | dirmode | outcome                            |
      | stat,remove      | 0775    | reaches the provider               |
      | stat,write       | 0775    | is denied before provider dispatch |
      | stat,remove      | 0755    | is denied before provider dispatch |

  @NS_AUTHZ_013
  Scenario: A read-only mount denies every mutation but keeps identity for reads
    Given "/data" is mounted read-only
    And the grants
      | kind | subject | path  | scope | rights                             |
      | user | alice   | /data | tree  | stat,walk,read,write,create,remove |
    Then alice is denied before provider dispatch for each of
      | operation                              |
      | open "/data/report" for OWRITE         |
      | open "/data/report" for OREAD,OTRUNC   |
      | open "/data/report" for OREAD,ORCLOSE  |
      | create file "new" in "/data"           |
      | remove "/data/report"                  |
    When alice opens "/data/report" for OREAD
    Then the open succeeds with the provider's identity for "/data/report"

  @NS_AUTHZ_014
  Scenario: Tree grants rely on the provider's parent relation
    Given the grants
      | kind | subject | path  | scope | rights    |
      | user | alice   | /data | tree  | stat,read |
    And the provider does not report parents
    When a view is built for alice
    Then construction fails because tree grants need provider ancestry

  @NS_AUTHZ_014
  Scenario: A parent cycle is denied rather than followed
    Given the grants
      | kind | subject | path  | scope | rights    |
      | user | alice   | /data | tree  | stat,read |
    And the provider reports "/archive" and "/archive/old" as each other's parent
    When alice opens "/archive/old" for OREAD
    Then the request reports not found

  @NS_AUTHZ_015
  Scenario: Moving an open object keeps its handle but not its new neighbours
    Given the grants
      | kind | subject | path  | scope | rights         |
      | user | alice   | /     | self  | walk           |
      | user | alice   | /data | tree  | stat,walk,read |
    And alice has opened "/data/report" for OREAD
    When "/data/report" moves to "/archive/report"
    Then alice can read through that handle
    When alice walks to "/archive/old"
    Then the walk reports not found

  @NS_AUTHZ_016
  Scenario: A claimed user in the operation context is ignored
    Given the grants
      | kind | subject | path  | scope | rights          |
      | user | glenda  | /data | tree  | stat,read,write |
      | user | alice   | /data | tree  | stat,read       |
    When alice opens "/data/report" for OWRITE with an operation context naming glenda
    Then the request is denied before provider dispatch

  @NS_AUTHZ_016
  Scenario: A handle that was never walked to gets no more than its grants
    Given the grants
      | kind | subject | path  | scope | rights    |
      | user | alice   | /data | tree  | stat,read |
    When alice opens the handle of "/archive/old" obtained elsewhere for OREAD
    Then the request reports not found

  @NS_AUTHZ_017
  Scenario: Stat needs the stat right and no mode permission
    Given "/data/private" has mode 0000
    And the grants
      | kind | subject | path          | scope | rights |
      | user | alice   | /data/private | self  | stat   |
      | user | bob     | /data/private | self  | read   |
    When alice stats "/data/private"
    Then the stat succeeds
    When bob stats "/data/private"
    Then the request is denied before provider dispatch

  @NS_AUTHZ_018
  Scenario: Listing a directory needs the read right
    Given the grants
      | kind | subject | path  | scope | rights    |
      | user | bob     | /data | self  | stat,walk |
    When bob lists the directory "/data" directly
    Then the request is a definite directory rejection

  @NS_AUTHZ_018
  Scenario: Reading a directory needs the read right and read permission
    Given the grants
      | kind | subject | path  | scope | rights         |
      | user | bob     | /data | self  | stat,walk,read |
    And "/data" has mode 0771
    When bob opens "/data" for OREAD
    Then the request is a definite directory rejection

  @NS_AUTHZ_019
  Scenario: The layer does not expose metadata updates
    When a view is built for alice
    Then the view offers no wstat or open-stat capability

  @NS_AUTHZ_020
  Scenario Outline: An invalid policy fails construction
    Given a policy with <defect>
    When the policy is constructed
    Then construction fails

    Examples:
      | defect                                 |
      | generation 0                           |
      | a grant with no rights                 |
      | the same grant twice                   |
      | a group member who is not a principal  |
      | two principals with the same user      |

  @NS_AUTHZ_001
  Scenario: Revocation also stops walks, listings and creates
    Given the grants
      | kind | subject | path  | scope | rights                           |
      | user | alice   | /     | tree  | stat,walk,read,write,create      |
    When the policy generation becomes 2
    And alice walks to "/data/report"
    Then the walk is denied at "/"
    When alice lists "/data" after revocation
    Then the request is denied before provider dispatch
    When alice creates file "new" in "/data" with permissions 0664 for OWRITE
    Then the request is a definite create rejection with no provider call

  @NS_AUTHZ_002
  Scenario Outline: The owner set alone can grant access
    Given the grants
      | kind | subject | path         | scope | rights    |
      | user | glenda  | /data/report | self  | stat,read |
    And "/data/report" has mode <mode>
    When glenda opens "/data/report" for OREAD
    Then the open <outcome>

    Examples:
      | mode | outcome                            |
      | 0400 | succeeds                           |
      | 0600 | succeeds                           |
      | 0200 | is denied before provider dispatch |

  @NS_AUTHZ_002
  Scenario: A nogroup member cannot list or walk from a non-directory through the other set
    Given group "nogroup" has members "carol"
    And the grants
      | kind | subject | path          | scope | rights         |
      | user | carol   | /archive      | self  | stat,walk,read |
      | user | carol   | /archive/old  | self  | stat,walk      |
    And "/archive" has mode 0004
    And "/archive/old" has mode 0001
    When carol lists the directory "/archive" directly
    Then the request is a definite directory rejection
    When carol walks out of the file "/archive/old"
    Then the request is denied before provider dispatch

  @NS_AUTHZ_003
  Scenario: A user grant to a name that is also a group applies only to that user
    Given the grants
      | kind | subject | path         | scope | rights    |
      | user | writers | /data/report | self  | stat,read |
    When alice opens "/data/report" for OREAD
    Then the request reports not found
    When writers opens "/data/report" for OREAD
    Then the open succeeds

  @NS_AUTHZ_005
  Scenario: Overlapping grants are combined, never cancelled
    Given the grants
      | kind  | subject | path         | scope | rights                      |
      | user  | alice   | /archive/old | self  | stat,read                   |
      | group | writers | /archive/old | self  | stat,read                   |
      | user  | alice   | /data        | tree  | stat,walk,read,write,create |
      | group | writers | /data        | tree  | stat,write                  |
    When alice opens "/archive/old" for OREAD
    Then the open succeeds
    When alice creates file "new" in "/data" with permissions 0664 for OWRITE
    Then the provider receives permissions 0664

  @NS_AUTHZ_005
  Scenario: Opening a hidden directory is a definite directory rejection
    Given the grants
      | kind | subject | path         | scope | rights    |
      | user | alice   | /data/report | self  | stat,read |
    When alice opens "/archive" for OREAD
    Then the request is a definite directory rejection

  @NS_AUTHZ_007
  Scenario Outline: Read-only mounts deny only modes that write, truncate or remove
    Given "/data" is mounted read-only
    And the grants
      | kind | subject | path  | scope | rights                       |
      | user | alice   | /data | tree  | stat,read,write,remove       |
    When alice opens "<path>" for <open>
    Then the open <outcome>

    Examples:
      | path         | open          | outcome                            |
      | /data/tools  | OEXEC         | succeeds                           |
      | /data/report | ORDWR         | is denied before provider dispatch |

  @NS_AUTHZ_007
  Scenario Outline: Truncation needs both the write right and write permission
    Given the grants
      | kind | subject | path  | scope | rights   |
      | user | alice   | /data | tree  | <rights> |
    And "/data/report" has mode <mode>
    When alice opens "/data/report" for <open>
    Then the open is denied before provider dispatch

    Examples:
      | rights          | mode | open          |
      | stat,read       | 0664 | OWRITE,OTRUNC |
      | stat,read,write | 0644 | OREAD,OTRUNC  |

  @NS_AUTHZ_008
  Scenario: Write-only and read-write handles carry exactly their modes
    Given the grants
      | kind | subject | path  | scope | rights          |
      | user | alice   | /data | tree  | stat,read,write |
    And alice has opened "/data/report" for OWRITE
    When alice reads through that handle
    Then the request is denied before provider dispatch
    When alice writes "x" through that handle
    Then the write reaches the provider
    Given alice has opened "/data/report" for ORDWR
    When alice writes "x" through that handle
    Then the write reaches the provider
    And alice can read through that handle

  @NS_AUTHZ_008
  Scenario: A view releases only handles it opened
    When alice clunks a handle opened directly from the provider
    Then the request is denied before provider dispatch

  @NS_AUTHZ_011
  Scenario: Creating without opening follows the same parent checks
    Given the grants
      | kind | subject | path  | scope | rights                      |
      | user | alice   | /data | tree  | stat,walk,read,write,create |
    When alice creates the entry "made" in "/data" without opening it
    Then the entry is created
    When alice creates the entry "denied" in "/archive" without opening it
    Then the request is a definite create rejection

  @NS_AUTHZ_012
  Scenario: Remove fails closed when the provider cannot attest a parent
    Given the provider does not report parents
    And the grants
      | kind | subject | path         | scope | rights      |
      | user | alice   | /data/report | self  | stat,remove |
    When alice removes "/data/report"
    Then the remove is denied before provider dispatch

  @NS_AUTHZ_013
  Scenario: A read-only mount does not restrict resources outside it
    Given "/data" is mounted read-only
    And the grants
      | kind | subject | path     | scope | rights          |
      | user | alice   | /archive | tree  | stat,read,write |
    When alice opens "/archive/old" for OWRITE
    Then the open succeeds

  @NS_AUTHZ_014
  Scenario Outline: Containment is proven for at most the depth bound of ancestors
    Given a chain of <depth> nested directories under "/" ending in a file
    And alice holds a read tree grant on "/"
    When alice opens the file at the end of the chain for OREAD
    Then the open <outcome>

    Examples:
      | depth | outcome           |
      | 63    | succeeds          |
      | 64    | reports not found |

  @NS_AUTHZ_012
  Scenario: Removing through an open handle releases it even when the remove is denied
    Given the grants
      | kind | subject | path  | scope | rights    |
      | user | alice   | /data | tree  | stat,read |
    And alice has opened "/data/report" for OREAD
    When alice removes "/data/report" through that handle
    Then the remove is denied before provider dispatch
    When alice reads through that handle
    Then the request is denied before provider dispatch
