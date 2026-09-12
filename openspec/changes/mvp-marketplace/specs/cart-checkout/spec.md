# Cart-Checkout Specification

## ADDED Requirements

### Requirement: Order Creation From Cart
The system MUST accept a checkout request containing a list of `{variantId, quantity}` pairs and a buyer email, and MUST create one `Order` with one `OrderItem` per line, in `Pending` status.

#### Scenario: Multi-item checkout
- GIVEN a cart with 2 distinct variants, one with quantity 2
- WHEN the buyer submits checkout
- THEN one `Order` is created with 2 `OrderItem` rows, one holding `Quantity=2`

### Requirement: Server-Side Price Recomputation
The system MUST recompute unit price and snapshot product/variant names from the current `ProductVariant` record and MUST ignore any price or name submitted by the client.

#### Scenario: Price tampering ignored
- GIVEN a variant's current price is 5000 ARS
- WHEN the client submits checkout with `unitPrice: 1` for that variant
- THEN the created `OrderItem.UnitPrice` is 5000 ARS, not 1

#### Scenario: Inactive or unknown variant rejected
- GIVEN a variant is inactive or does not exist
- WHEN checkout is submitted referencing that variant
- THEN the response is 422 Problem Details and no order is created

### Requirement: Guest and Authenticated Checkout
The system MUST allow checkout without authentication provided a buyer email is supplied, and MUST link `Order.UserId` to the authenticated Supabase `sub` when a valid JWT is present, even if the submitted email differs from the account email.

#### Scenario: Guest checkout
- GIVEN no Authorization header is sent
- WHEN checkout is submitted with a valid email
- THEN the order is created with `UserId=null` and `BuyerEmail` set to the submitted email

#### Scenario: Missing email on guest checkout
- GIVEN no Authorization header is sent
- WHEN checkout is submitted without an email
- THEN the response is 422 Problem Details

#### Scenario: Authenticated checkout with edited email
- GIVEN a valid JWT for `sub=user-123` whose account email is `a@x.com`
- WHEN checkout is submitted with email `b@x.com`
- THEN the order is created with `UserId=user-123` and `BuyerEmail=b@x.com`

### Requirement: Mercado Pago Preference Creation
The system MUST create exactly one Mercado Pago Checkout Pro preference per order, containing one preference item per `OrderItem`, and MUST persist the resulting `MpPreferenceId` on the order.

#### Scenario: Preference item count matches order
- GIVEN an order with 2 `OrderItem` rows
- WHEN the preference is created
- THEN the Mercado Pago preference contains exactly 2 items and the order stores the returned preference id
