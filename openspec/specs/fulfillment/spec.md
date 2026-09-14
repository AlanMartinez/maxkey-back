# Fulfillment Specification

> Seeded from `openspec/changes/mvp-marketplace/specs/fulfillment/spec.md` on 2026-09-14 when the admin-dashboard change was archived. The "One-Time Delivery Email" requirement was updated to include the explicit admin-resend path. Other mvp-marketplace domains will be seeded when that change is archived.

## Requirements

### Requirement: Admin Order Listing
The system MUST expose `GET /admin/orders?status=AwaitingFulfillment` returning orders in that status with per-item fulfillment progress (keys assigned vs. quantity required), restricted to admins.

#### Scenario: List shows per-item progress
- GIVEN an `AwaitingFulfillment` order with items having 1/2 and 3/3 keys assigned
- WHEN an admin requests the listing
- THEN the response includes both items with their assigned/required counts

### Requirement: Key Attachment
The system MUST expose `POST /admin/orders/{id}/items/{itemId}/keys` to attach exactly one key code to an order item, restricted to admins, and MUST reject the request with 409 when the item already has `Quantity` keys assigned.

#### Scenario: Attach within quantity
- GIVEN an item requires 3 keys and has 2 assigned
- WHEN an admin attaches one more key
- THEN the item has 3 keys assigned

#### Scenario: Reject over-quantity attach
- GIVEN an item requires 3 keys and already has 3 assigned
- WHEN an admin attempts to attach a 4th key
- THEN the response is 409 and no key row is added

#### Scenario: Concurrent double-attach on the same item
- GIVEN an item needs 1 more key to reach `Quantity`
- WHEN two admins submit attach requests for that item at the same time
- THEN exactly one request succeeds and the other receives 409, with no over-assignment

### Requirement: All-or-Nothing Delivery Derivation
The system MUST derive order completion from key counts: an order transitions to `Delivered` only when every `OrderItem` has `Assigned` keys equal to its `Quantity`. Buyers MUST NOT see any keys before that point.

#### Scenario: Partial fulfillment stays awaiting
- GIVEN an order with 2 items, one fully keyed (3/3) and one partial (2/3)
- WHEN the admin attaches keys such that only one item is complete
- THEN the order remains `AwaitingFulfillment` and the buyer's order detail shows no key codes

#### Scenario: Last key completes delivery
- GIVEN an order with all items at `Quantity-1` assigned keys
- WHEN the admin attaches the final missing key for the last incomplete item
- THEN the order transitions to `Delivered`, `DeliveredAt` is set, and all keys become visible to the buyer

### Requirement: Key Encryption at Rest
The system MUST store key codes AES-256-GCM encrypted with an associated `KeyVersion`, MUST NOT log plaintext codes, and MUST decrypt only for buyer order detail and the delivery email.

#### Scenario: Plaintext never persisted
- GIVEN an admin attaches a key code
- WHEN the row is persisted
- THEN the stored value is ciphertext (nonce, tag, ciphertext) with a `KeyVersion`, not the plaintext code

### Requirement: One-Time Delivery Email
The system MUST send the delivery email containing all keys exactly once per `AwaitingFulfillment → Delivered` transition, emitted via the `OrderDelivered` outbox event inserted in the same transaction as that transition. Handler retries MAY duplicate the email if the send succeeds but marking the event `Processed` fails (at-least-once). An admin MAY explicitly trigger a resend for an order already in `Delivered` status via `POST /admin/orders/{id}/resend-delivery`; each such resend MUST be recorded with the acting admin's `sub` and MUST NOT alter `DeliveredAt` or the order's keys.

#### Scenario: Email sent once even under retry
- GIVEN the `Delivered` transition is processed and, due to a retried request or duplicate event, the same transition is evaluated again
- WHEN the second evaluation occurs
- THEN no second delivery email is sent

#### Scenario: Admin resend for a delivered order
- GIVEN an order with `Status=Delivered`
- WHEN an admin calls the resend-delivery endpoint
- THEN the same delivery email content is sent again and the resend is logged with the admin's `sub`

#### Scenario: Resend rejected outside Delivered status
- GIVEN an order with `Status=AwaitingFulfillment`
- WHEN an admin calls the resend-delivery endpoint for it
- THEN the response is 409 and no email is sent

### Requirement: Admin Authorization
Admin endpoints MUST require a valid Supabase JWT whose `sub` is present in a configured admin allowlist. An empty allowlist MUST fail closed (403 for all requests).

#### Scenario: Non-admin rejected
- GIVEN a valid JWT for a `sub` not in the allowlist
- WHEN that user calls an admin endpoint
- THEN the response is 403

#### Scenario: Empty allowlist fails closed
- GIVEN the admin allowlist configuration is empty
- WHEN any user, including a valid Supabase user, calls an admin endpoint
- THEN the response is 403
