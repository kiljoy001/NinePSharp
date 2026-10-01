@plan9_namespace @fid_lifetime @NS_FID_001
Feature: Namespace fids survive unmount
  Unmount removes a namespace route, not the resource selected by an existing
  fid. These scenarios assume the provider remains available and permits I/O.

  Scenario Outline: Retained file fids survive namespace removal and replacement
    Given a mounted file selected by an <state> fid
    When the mount is <change>
    Then the retained fid still reads and writes the original file
    And a fresh walk selects the <visible> file
    And unmount has not clunked the retained file handle
    When the retained fid is released by <cleanup>
    Then its provider handle is clunked once and the fid is invalid

    Examples:
      | state    | change               | visible     | cleanup    |
      | open     | completely unmounted | underlying  | clunk      |
      | unopened | completely unmounted | underlying  | clunk      |
      | open     | selectively unmounted| underlying  | clunk      |
      | unopened | selectively unmounted| underlying  | clunk      |
      | open     | unmounted and replaced | replacement | clunk    |
      | unopened | unmounted and replaced | replacement | clunk    |
      | open     | completely unmounted | underlying  | disconnect |
      | unopened | completely unmounted | underlying  | disconnect |
      | open     | selectively unmounted| underlying  | disconnect |
      | unopened | selectively unmounted| underlying  | disconnect |
      | open     | unmounted and replaced | replacement | disconnect |
      | unopened | unmounted and replaced | replacement | disconnect |
