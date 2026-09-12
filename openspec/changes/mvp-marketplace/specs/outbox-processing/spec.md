# Outbox-Processing Specification

## ADDED Requirements

### Requirement: Batch Claim With Row Locking
The system MUST claim a batch of due `OutboxEvent` rows using `SELECT ... FOR UPDATE SKIP LOCKED` and MUST mark claimed rows `Processing` in the same transaction as the claim.

#### Scenario: Claim marks rows Processing atomically
- GIVEN 3 `OutboxEvent` rows are `Pending` and due
- WHEN the processor claims a batch
- THEN all 3 rows are `Processing` immediately after the claim transaction commits

### Requirement: Multi-Instance Claim Safety
When multiple processor instances poll concurrently, the system MUST ensure no `OutboxEvent` row is claimed and processed by more than one instance.

#### Scenario: Two instances poll the same due rows
- GIVEN two processor instances poll at the same time and rows are locked by instance A
- WHEN instance B attempts to claim the same rows
- THEN instance B's `SKIP LOCKED` query excludes those rows and processes none of them twice

### Requirement: OrderApproved Handler
The system MUST handle `OrderApproved` events by transitioning the order from `Paid` to `AwaitingFulfillment` and sending an operator notification (email or log).

#### Scenario: Handler advances order status
- GIVEN an `OrderApproved` `OutboxEvent` for an order in `Paid` status
- WHEN the handler processes the event
- THEN the order is `AwaitingFulfillment` and an operator notification is recorded/sent, and the event is marked `Processed`

### Requirement: Exponential Backoff on Failure
On handler failure, the system MUST reschedule the event with `NextAttemptAt = now + 30s * 2^Attempts` and increment `Attempts`.

#### Scenario: First failure reschedules
- GIVEN an `OutboxEvent` with `Attempts=0` fails during handling
- WHEN the failure is recorded
- THEN `Attempts=1` and `NextAttemptAt` is approximately 60s in the future, and the event returns to a retryable state

### Requirement: Dead-Letter After Max Attempts
The system MUST mark an `OutboxEvent` as `Failed` (dead-letter, no further retries) after its 8th failed attempt.

#### Scenario: Eighth failure dead-letters the event
- GIVEN an `OutboxEvent` has `Attempts=7` and fails again
- WHEN the failure is recorded
- THEN the event status is `Failed` and it is excluded from future claim batches
