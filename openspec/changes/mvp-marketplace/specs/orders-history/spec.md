# Orders-History Specification

## ADDED Requirements

### Requirement: My Orders Listing
The system MUST expose an authenticated `GET /me/orders` endpoint returning only orders where `Order.UserId` matches the caller's `sub`.

#### Scenario: List shows only own orders
- GIVEN user A has 2 orders and user B has 1 order
- WHEN user A requests `/me/orders`
- THEN only user A's 2 orders are returned

#### Scenario: Anonymous access rejected
- GIVEN no Authorization header
- WHEN a request is made to `/me/orders`
- THEN the response is 401

### Requirement: Order Detail With Conditional Key Reveal
The system MUST expose an authenticated order detail endpoint scoped to the owning user, and MUST include decrypted key codes only when the order status is `Delivered`.

#### Scenario: Delivered order reveals keys
- GIVEN an order owned by the caller with status `Delivered`
- WHEN the caller requests that order's detail
- THEN the response includes decrypted key codes for every item

#### Scenario: Non-delivered order hides keys
- GIVEN an order owned by the caller with status `AwaitingFulfillment` and 2 of 3 keys assigned
- WHEN the caller requests that order's detail
- THEN the response includes no key codes, regardless of per-item progress

### Requirement: Ownership Enforcement
The system MUST NOT expose another user's order data; requests for an order not owned by the caller MUST return 404.

#### Scenario: Cross-user access denied
- GIVEN order O1 is owned by user A
- WHEN user B, authenticated, requests order O1's detail
- THEN the response is 404, not the order data

Guest order lookup is explicitly out of scope for this change (Non-goal, see proposal).
