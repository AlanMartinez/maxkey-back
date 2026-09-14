# Admin Catalog Specification

## Purpose

Let admins list and edit the product catalog (including inactive items) without a seed-file redeploy, reusing `Product.UpdateCatalogInfo` / `ProductVariant.UpdateDetails` behind `AdminPolicy`.

## Requirements

### Requirement: Admin Product Listing Including Inactive
The system MUST expose an admin endpoint that lists all products regardless of `IsActive`, including their variants, restricted to `AdminPolicy`.

#### Scenario: Inactive products included
- GIVEN products exist with `IsActive=true` and `IsActive=false`
- WHEN an admin requests the catalog list
- THEN both active and inactive products are returned

### Requirement: Admin Product Content Update
The system MUST let an admin update a product's `Description` and `ImageKey`, validating the same non-empty invariants as `UpdateCatalogInfo`.

#### Scenario: Description and ImageKey updated
- GIVEN a product with an existing `ImageKey`
- WHEN an admin submits a new `Description` and `ImageKey`
- THEN the product reflects both new values on the next read

#### Scenario: Invalid update rejected
- GIVEN a product exists
- WHEN an admin submits an empty required field
- THEN the response is 422 Problem Details (domain validation) and the product is unchanged

### Requirement: Admin Product Activation Toggle
The system MUST let an admin set a product's `IsActive` flag independently of other fields.

#### Scenario: Product deactivated
- GIVEN an active product
- WHEN an admin toggles it to `IsActive=false`
- THEN the product no longer appears in the public product listing

### Requirement: Admin Variant Activation Toggle
The system MUST let an admin set a `ProductVariant`'s `IsActive` flag independently of its price and other fields.

#### Scenario: Variant deactivated
- GIVEN an active variant on a product with other active variants
- WHEN an admin toggles it to `IsActive=false`
- THEN the variant no longer appears in the public product detail response

### Requirement: Admin Catalog Authorization
Admin catalog endpoints MUST require `AdminPolicy` (valid JWT with `sub` in `Auth:AdminSubs`).

#### Scenario: Unauthenticated request rejected
- GIVEN no Authorization header
- WHEN a client calls an admin catalog endpoint
- THEN the response is 401

#### Scenario: Non-admin rejected
- GIVEN a valid JWT whose `sub` is not in `Auth:AdminSubs`
- WHEN that user calls an admin catalog endpoint
- THEN the response is 403
