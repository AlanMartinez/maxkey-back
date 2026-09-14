# Admin Buyers Specification

## Purpose

Give admins a read view of buyers (grouped by `BuyerEmail`) with their orders, items, and keys, plus an explicit, audited delivery-email resend — without adding revoke/reissue/ban or a Users entity.

## Requirements

### Requirement: Buyer Listing Grouped By Email
The system MUST expose a paginated admin endpoint listing buyers grouped by `BuyerEmail`, each with their orders (`Status`), order items, and assigned keys, filterable by an email search term, restricted to `AdminPolicy`.

#### Scenario: Buyer with multiple orders grouped together
- GIVEN two orders exist for the same `BuyerEmail`
- WHEN an admin requests the buyer listing
- THEN both orders appear nested under one buyer entry

#### Scenario: Email search narrows results
- GIVEN buyers `a@x.com` and `b@x.com` exist
- WHEN an admin searches `q=a@`
- THEN only `a@x.com`'s entry is returned

#### Scenario: Pagination bounds page size
- GIVEN more buyers exist than one page holds
- WHEN an admin requests a page
- THEN the response includes only that page's buyers plus pagination metadata

### Requirement: Key Exposure in Buyer View
The system MUST NOT return key codes (plaintext or masked) in the buyer listing; it exposes only the count of assigned keys per order item.

#### Scenario: Key code not exposed
- GIVEN an order item has an assigned key
- WHEN an admin requests the buyer listing
- THEN the response contains the assigned key count for the item and no key code value

### Requirement: Resend Delivery Email
The system MUST expose `POST /admin/orders/{id}/resend-delivery`, restricted to `AdminPolicy`, that re-sends the delivery email for an order only when `Status=Delivered`, and MUST record the acting admin's `sub` for audit. No rate limit applies.

#### Scenario: Resend for a delivered order
- GIVEN an order with `Status=Delivered`
- WHEN an admin calls resend-delivery
- THEN the delivery email is sent again and the action is logged with the admin's `sub`

#### Scenario: Resend rejected for non-delivered order
- GIVEN an order with `Status=AwaitingFulfillment`
- WHEN an admin calls resend-delivery for it
- THEN the response is 409 and no email is sent

### Requirement: Admin Buyers Authorization
Admin buyer endpoints MUST require `AdminPolicy`.

#### Scenario: Non-admin rejected
- GIVEN a valid JWT whose `sub` is not in `Auth:AdminSubs`
- WHEN that user calls an admin buyers endpoint
- THEN the response is 403
