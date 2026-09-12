# Payments-Webhook Specification

## ADDED Requirements

### Requirement: Signature Validation Before Any I/O
The system MUST validate the `x-signature` header using HMAC-SHA256 over `id:<data.id>;request-id:<x-request-id>;ts:<ts>;` before performing any database read, write, or external call, and MUST return 401 with no state change when validation fails.

#### Scenario: Invalid signature rejected
- GIVEN a webhook request with a tampered `x-signature`
- WHEN the endpoint receives it
- THEN the response is 401 and no `ProcessedWebhookNotification`, `Order`, or `OutboxEvent` row is created or modified

### Requirement: Kill Switch
The system MUST return 503 for all webhook requests when `Payments:WebhookEnabled=false`, without evaluating signature or dedupe.

#### Scenario: Webhook disabled
- GIVEN `Payments:WebhookEnabled=false`
- WHEN a valid, signed webhook request arrives
- THEN the response is 503 and no state changes occur

### Requirement: Notification Deduplication
The system MUST record each processed notification by `x-request-id` in `ProcessedWebhookNotification` and MUST treat a repeated `x-request-id` as a no-op.

#### Scenario: Exact replay is a no-op
- GIVEN a notification with `x-request-id=R1` was already processed and the order transitioned to `Paid`
- WHEN a request with the identical `x-request-id=R1` arrives again
- THEN the response is accepted (2xx) and no additional state change or `OutboxEvent` is created

### Requirement: Authoritative Payment Fetch
The system MUST always fetch the payment resource from the Mercado Pago API by id and MUST NOT trust the notification body's status field.

#### Scenario: Body status ignored
- GIVEN a notification body claims `status=approved` for a payment that is actually `pending` per the MP API
- WHEN the webhook processes it
- THEN the order is NOT transitioned to `Paid`

### Requirement: Idempotent State Transition
The system MUST guard the `Pending → Paid` transition so that a re-notification for an order already in `Paid` or later status is a no-op, even under a different `x-request-id` for the same payment.

#### Scenario: Re-notification after Paid
- GIVEN an order already in `Paid` status
- WHEN a new webhook notification for the same payment (different `x-request-id`) confirms `approved` again
- THEN no second state transition and no second `OrderApproved` `OutboxEvent` are created

#### Scenario: Concurrent duplicate delivery of the same approval
- GIVEN an order is `Pending` and Mercado Pago delivers two notifications for the same approved payment, each with a different `x-request-id`, at the same instant
- WHEN both requests are processed concurrently
- THEN exactly one `Pending → Paid` transition occurs and exactly one `OrderApproved` `OutboxEvent` is created

### Requirement: Rejected Payment Handling
When the payment fetched from the MP API has status `rejected` for an order in `Pending`, the system MUST keep the order `Pending` (Mercado Pago Checkout Pro lets the buyer retry payment under the same preference), MUST record the rejected `MpPaymentId` on the order as the last payment attempt for visibility, MUST NOT create an `OutboxEvent`, and MUST NOT transition the order to `Cancelled`. Statuses `pending` and `in_process` MUST likewise be recorded on the order with no state transition.

#### Scenario: Rejection followed by a later approval
- GIVEN an order is `Pending` and a webhook notification reports the fetched payment as `rejected`
- WHEN the order's `MpPaymentId` is recorded for that rejected payment and a later webhook notification reports a new payment on the same preference as `approved`
- THEN the order transitions `Pending → Paid` exactly once, and no `Cancelled` state or `OutboxEvent` was ever produced for the rejection

### Requirement: Transactional Outbox Insert on Approval
The system MUST set `Order.Status=Paid` and insert an `OrderApproved` `OutboxEvent` in the same database transaction.

#### Scenario: Approved payment creates paired state
- GIVEN a payment fetched from the MP API has status `approved`
- WHEN the webhook processes it for the first time
- THEN `Order.Status=Paid` and exactly one `OrderApproved` `OutboxEvent` exist, committed atomically

### Requirement: Non-Actionable Status Handling
The system MUST log a generic ignored-status entry, with no order state change, for MP statuses other than `approved`, `rejected`, `pending`, or `in_process` (e.g. `refunded`, `charged_back`). Refund and chargeback handling is out of scope.

#### Scenario: Refunded status ignored
- GIVEN a payment fetched from the MP API has status `refunded`
- WHEN the webhook processes it
- THEN no order state change occurs and one log entry recording the ignored status is produced
