Feature: Failure-correct distributed resource operations
  Resource grains preserve Plan 9 data operations while Orleans may reactivate them.

  Scenario: An open mounted resource supports file IO
    Given a mounted distributed resource session
    When the session walks to job and opens it read-write
    And the session writes distributed payload
    Then the session reads distributed payload
    And the mounted stat name remains job

  Scenario: Retrying a create after grain migration has one effect
    Given a distributed resource root
    When create operation 10 creates once
    And the resource grain is migrated to another silo
    And create operation 10 is replayed
    Then the two create responses identify the same open handle
    And the resource contains one once child
    And the resource records one mutation

  Scenario: Retrying a write after grain migration has one effect
    Given an open distributed resource file
    When write operation 20 stores payload
    And the resource grain is migrated to another silo
    And write operation 20 is replayed
    Then both writes report the same count
    And the resource records two mutations

  Scenario: Reusing an operation identity for different data is rejected
    Given an open distributed resource file
    When write operation 20 stores payload
    And write operation 20 is reused with different data
    Then the operation identity collision is rejected
