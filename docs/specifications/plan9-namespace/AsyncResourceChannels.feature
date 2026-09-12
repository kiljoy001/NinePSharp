@plan9_namespace @async_resources @extension
Feature: Asynchronous message resources in a Plan 9 namespace
  A Plan 9 channel identifies a resource. A CSP-style channel is a separate
  resource that transports typed messages without blocking the Orleans scheduler.

  @NS_ASYNC_001
  Scenario: A message channel is addressable through the namespace
    Given a message channel grain published at /apps/events
    When a 9P client walks to /apps/events
    Then it receives an ordinary resource channel
    And the channel exposes send, receive, and status operations

  @NS_ASYNC_002
  Scenario: Sending a message returns without waiting for a receiver
    Given an asynchronous message channel with no active receiver
    When a client writes one valid message
    Then the write completes without blocking the namespace scheduler
    And the message is retained according to the channel delivery policy

  @NS_ASYNC_003
  Scenario: A receiver obtains a previously sent message
    Given a message channel containing one retained message
    When a receiver reads from the channel
    Then it receives that message exactly once
    And the channel reports the updated pending count

  @NS_ASYNC_004
  Scenario: A bounded channel applies backpressure
    Given a message channel with a capacity of two messages
    When a client sends three messages without a receiver
    Then the first two messages are accepted
    And the third send returns a bounded backpressure result
    And the sender remains free to perform unrelated namespace operations

  @NS_ASYNC_005
  Scenario: A pending receive completes when a message arrives
    Given a receiver waiting on an empty message channel
    When another client sends a valid message
    Then the pending receive completes with that message
    And no polling loop is required

  @NS_ASYNC_006
  Scenario: Cancellation releases a pending message operation
    Given a receiver waiting on an empty message channel with a cancellation token
    When the token is cancelled
    Then the receive completes as cancelled
    And the channel has no leaked waiter

  @NS_ASYNC_007
  Scenario: A completed operation can be read later by operation identifier
    Given a grain operation that produces a deferred result
    When a client submits the operation and disconnects
    And another client reads its operation identifier
    Then the later client can obtain the recorded result or failure
    And the operation is executed at most once

  @NS_ASYNC_008
  Scenario: Message delivery does not grant namespace authority
    Given a client that can send messages to an application channel
    When it sends a message containing another resource path or process identifier
    Then the receiver treats that value as application data
    And the sender gains no authority over the named resource
