# Delta for Fulfillment

## MODIFIED Requirements

### Requirement: One-Time Delivery Email
The system MUST send the delivery email containing all keys exactly once per `AwaitingFulfillment → Delivered` transition, emitted via the `OrderDelivered` outbox event inserted in the same transaction as that transition. Handler retries MAY duplicate the email if the send succeeds but marking the event `Processed` fails (at-least-once). An admin MAY explicitly trigger a resend for an order already in `Delivered` status via `POST /admin/orders/{id}/resend-delivery`; each such resend MUST be recorded with the acting admin's `sub` and MUST NOT alter `DeliveredAt` or the order's keys.
(Previously: no explicit resend path existed; only the automatic transition-triggered send was specified.)

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
