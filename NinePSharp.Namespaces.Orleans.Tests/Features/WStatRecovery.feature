@plan9_namespace @distributed_extension @wstat_recovery
Feature: Durable wstat recovery resolves ambiguous replies safely
  The journal and provider use one operation identity from admission through reconciliation.

  @NS_META_035 @NS_LIFE_014
  Scenario: A lost success reply is reconciled after gateway recreation
    Given a durable wstat operation targeting the distributed job file
    And the provider will lose the next successful wstat reply
    When the gateway submits a mode change with operation 41
    Then operation 41 reports recovery pending and its journal intent remains pending
    When a new gateway instance recovers operation 41
    Then the original provider result is returned
    And operation 41 is durably committed with one provider mutation

  @NS_META_035 @NS_LIFE_014
  Scenario: Reusing a pending operation identity for different metadata is rejected
    Given a durable wstat operation targeting the distributed job file
    And the provider will lose the next successful wstat reply
    When the gateway submits a mode change with operation 42
    And operation 42 is resubmitted with a different mode
    Then the operation identity collision is rejected without another provider mutation

  @NS_META_035 @NS_META_036
  Scenario: Recovery uses the selected resource rather than a replacement pathname
    Given a durable path wstat operation targeting the distributed job file
    And the provider will lose the next successful wstat reply
    When the gateway submits a mode change with operation 43
    And a replacement resource becomes visible at the original path
    And the resource grain migrates before recovery
    When a new gateway instance recovers operation 43
    Then recovery changes only the originally selected resource
    And the replacement resource remains unchanged
