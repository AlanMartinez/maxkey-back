# Delta for Admin Catalog

## ADDED Requirements

### Requirement: Admin Product Creation
The system MUST expose `POST /admin/catalog/products` accepting `{ slug, name, platform, description?, imageKey?, isActive }`. The system MUST validate slug uniqueness and reject duplicates. The system MUST validate the same non-empty invariants as `Product`'s constructor (slug lowercase, name/platform non-empty).

#### Scenario: Product created
- GIVEN a valid, unique slug and required fields
- WHEN an admin submits the create-product request
- THEN the response is 201 with the created `AdminProduct`

#### Scenario: Duplicate slug rejected
- GIVEN a product already exists with slug `witcher-3`
- WHEN an admin submits a create request with the same slug
- THEN the response is 409 and no product is created

#### Scenario: Invalid fields rejected
- GIVEN a create request with an empty `name` or non-lowercase `slug`
- WHEN the admin submits it
- THEN the response is 422 Problem Details and no product is created

### Requirement: Admin Product Soft-Delete
The system MUST expose `DELETE /admin/catalog/products/{id}` that sets `IsActive=false`. The system MUST NOT hard-delete the row.

#### Scenario: Product soft-deleted
- GIVEN an active product with `id`
- WHEN an admin calls delete on that product
- THEN the response is 200 with the updated `AdminProduct` (`IsActive=false`)
- AND the product no longer appears in the public product listing

#### Scenario: Product not found
- GIVEN no product exists with `id`
- WHEN an admin calls delete on that `id`
- THEN the response is 404

### Requirement: Admin Variant Creation
The system MUST expose `POST /admin/catalog/products/{productId}/variants` accepting `{ region?, edition?, price, discountPercentage?, currency, sortOrder, isActive }`. The system MUST validate the parent product exists and reject invariants the same way as `ProductVariant`'s constructor.

#### Scenario: Variant created
- GIVEN an existing product and a valid variant payload
- WHEN an admin submits the create-variant request
- THEN the response is 201 with the created `AdminVariant`

#### Scenario: Parent product not found
- GIVEN no product exists with `productId`
- WHEN an admin submits a create-variant request for it
- THEN the response is 404 and no variant is created

#### Scenario: Invalid invariant rejected
- GIVEN a variant payload with a non-positive `price` or invalid `currency`
- WHEN the admin submits it
- THEN the response is 422 Problem Details and no variant is created

### Requirement: Admin Variant Soft-Delete
The system MUST expose `DELETE /admin/catalog/variants/{id}` that sets `IsActive=false`. The system MUST NOT hard-delete the row.

#### Scenario: Variant soft-deleted
- GIVEN an active variant with `id`
- WHEN an admin calls delete on that variant
- THEN the response is 200 with the updated `AdminVariant` (`IsActive=false`)

#### Scenario: Variant not found
- GIVEN no variant exists with `id`
- WHEN an admin calls delete on that `id`
- THEN the response is 404

### Requirement: Variant Currency Whitelist
`ProductVariant.Currency` MUST be one of `ARS`, `USD`, validated identically on create and update.

#### Scenario: Whitelisted currency accepted
- GIVEN a variant create or update payload with `currency=USD`
- WHEN the request is submitted
- THEN the variant is persisted with `Currency=USD`

#### Scenario: Non-whitelisted currency rejected
- GIVEN a variant create or update payload with `currency=EUR`
- WHEN the request is submitted
- THEN the response is 422 Problem Details and no change is persisted

### Requirement: Variant Discount Percentage Pricing
`ProductVariant` MUST accept an optional `DiscountPercentage` (`0 < pct < 100` when present) as the sole discount input on create and update. `OldPrice` MUST NOT be an accepted write input. Every read DTO (`AdminVariant` and the public catalog DTOs used by `GetCatalog`/`GetProductBySlug`) MUST compute `OldPrice = Price / (1 - DiscountPercentage/100)` when `DiscountPercentage` is set, else `null`.

#### Scenario: Discount percentage accepted, OldPrice computed
- GIVEN a variant created with `price=1000`, `discountPercentage=20`
- WHEN an admin or a public client reads that variant
- THEN both responses show `oldPrice=1250`

#### Scenario: No discount, OldPrice null
- GIVEN a variant created without `discountPercentage`
- WHEN any client reads that variant
- THEN `oldPrice` is `null`

#### Scenario: Out-of-range percentage rejected
- GIVEN a variant payload with `discountPercentage=0` or `discountPercentage=100`
- WHEN the request is submitted
- THEN the response is 422 Problem Details and no change is persisted

#### Scenario: OldPrice write input rejected
- GIVEN a variant update payload containing `oldPrice`
- WHEN the request is submitted
- THEN the field is ignored (not bound) and the request MUST use `discountPercentage` to affect pricing

### Requirement: Uniform Soft-Delete Guarantee
Deleting a `Product` or `ProductVariant` MUST always soft-delete (`IsActive=false`), regardless of whether `Key` or `OrderItem` rows reference it. The system MUST NOT branch on association state to decide between hard and soft delete.

#### Scenario: Referenced variant still soft-deletes
- GIVEN a variant referenced by an existing `OrderItem` and by a `Key`
- WHEN an admin deletes that variant
- THEN the response is 200, `IsActive=false`, and the row and its references remain intact

### Requirement: Catalog Seeder Discount Alignment
`CatalogSeeder`'s seed JSON format MUST express discounts via `DiscountPercentage` instead of `OldPrice`, matching the domain constructor contract, since the seeder is the only other writer of `ProductVariant` besides the admin endpoints.

#### Scenario: Seeded variant computes OldPrice on read
- GIVEN a seed entry with `discountPercentage=15` and `price=850`
- WHEN the catalog is seeded and then read via any catalog endpoint
- THEN the variant's computed `oldPrice` matches `850 / (1 - 0.15)`

## MODIFIED Requirements

### Requirement: Admin Catalog Authorization
Admin catalog endpoints MUST require `AdminPolicy` (valid JWT with `sub` in `Auth:AdminSubs`), including `POST /admin/catalog/products`, `DELETE /admin/catalog/products/{id}`, `POST /admin/catalog/products/{productId}/variants`, and `DELETE /admin/catalog/variants/{id}`.
(Previously: scope was limited to the existing list+update admin endpoints; now explicitly extended to the four new create/soft-delete endpoints.)

#### Scenario: Unauthenticated request rejected
- GIVEN no Authorization header
- WHEN a client calls an admin catalog endpoint
- THEN the response is 401

#### Scenario: Non-admin rejected
- GIVEN a valid JWT whose `sub` is not in `Auth:AdminSubs`
- WHEN that user calls an admin catalog endpoint
- THEN the response is 403

#### Scenario: New endpoints enforce the same policy
- GIVEN a valid JWT whose `sub` is not in `Auth:AdminSubs`
- WHEN that user calls `POST /admin/catalog/products` or any of the other 3 new endpoints
- THEN the response is 403

## Key Learnings

1. The existing admin-catalog spec had no currency or discount-pricing requirements, so both became ADDED rather than MODIFIED.
2. The proposal's Locked Constraints table (#4) confirms delete use cases reuse existing update methods with `isActive=false`, informing the soft-delete requirement wording.
3. `CatalogSeeder` is explicitly listed as Modified in the proposal's Affected Areas table, confirming the seed-format field rename is in-scope, not deferred.
4. `Admin Product Activation Toggle` and `Admin Variant Activation Toggle` requirements remain unchanged since PUT-based toggling behavior is untouched by this proposal.
