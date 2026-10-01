@plan9_namespace @file_wstat
Feature: Processes change native file metadata atomically

  @NS_META_016
  Scenario: Wstat validates its raw record before looking up the target
    Given a malformed wstat record and an absent path
    When wstat is attempted on that path
    Then bad stat wins and no metadata update reaches a provider

  @NS_META_018 @NS_META_019 @NS_META_024
  Scenario: An all-sentinel update is dispatched and returns the device result
    Given a valid all-sentinel wstat record
    When fwstat submits it to an open file whose provider returns 73
    Then fwstat returns 73 and keeps the descriptor installed

  @NS_META_020 @NS_META_021
  Scenario: An open mount point retains its rename restriction after unmount
    Given a directory descriptor opened on a mount point
    When that mount is removed and fwstat requests a nonempty name
    Then fwstat rejects the mount-point rename before provider dispatch

  @NS_META_027 @NS_META_028
  Scenario: A provider applies rename mode and length as one update
    Given an open file containing twenty bytes
    When fwstat renames it to final and changes mode to 0600 and length to 3
    Then the provider exposes all three changes together
