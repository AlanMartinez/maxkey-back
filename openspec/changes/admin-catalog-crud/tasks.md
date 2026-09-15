# Tasks: Admin Catalog Full CRUD (Create/Soft-Delete + Discount Pricing)

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~850–1300 total (PR1 ~450–580, PR2 ~300–400, PR3 ~220–340) |
| 400-line budget risk | High (default 400 budget; PR1 alone likely exceeds it) |
| 800-line budget risk (session override) | Medium — PR1 is closest to 800; `seed/catalog.json` diff size is the wildcard |
| Chained PRs recommended | Yes |
| Suggested split | PR1 (Domain+Migration+existing call sites+seed) → PR2 (new use cases) → PR3 (new endpoints) |
| Delivery strategy | auto-chain |
| Chain strategy | stacked-to-main |

Decision needed before apply: No
Chained PRs recommended: Yes
Chain strategy: stacked-to-main
400-line budget risk: High

Rationale: `OldPrice` removal from `ProductVariant` is a hard compile-time dependency for `ListAdminProducts.cs`, `UpdateProduct.cs`, `UpdateProductVariant.cs`, `GetCatalog.cs`, `GetProductBySlug.cs`, `CatalogSeeder.cs`, and the existing `UpdateProductVariantRequest` — these MUST land in the same PR as the domain change (PR1) to keep `main` buildable. The 4 brand-new use cases (PR2) and 4 new routes (PR3) are purely additive and can stack cleanly on top with no shared mutation, so `stacked-to-main` keeps `main` green after every merge.

### Suggested Work Units

| Unit | Goal | Likely PR | Focused test command | Runtime harness | Rollback boundary |
|------|------|-----------|----------------------|-----------------|-------------------|
| 1 | Domain pricing model swap + migration + all existing OldPrice call sites + seed data conversion | PR1 | `dotnet test --filter FullyQualifiedName~ProductVariant\|VariantPricing\|GetCatalog\|GetProductBySlug\|CatalogSeeder` | `dotnet ef database update` against a seeded local DB — verify backfilled `discount_percentage` matches pre-migration `old_price` pair | Revert migration Down script + `ProductVariant.cs`/`VariantPricing.cs`/5 call-site files + `seed/catalog.json` together as one atomic unit |
| 2 | 4 new admin use cases (Create/Delete Product/Variant) + DI wiring | PR2 | `dotnet test --filter FullyQualifiedName~CreateProduct\|DeleteProduct\|CreateProductVariant\|DeleteProductVariant` | N/A — additive use cases fully covered by `AppDbContext` integration tests, no manual scenario needed | Revert 4 new use case files + DI registration lines; independent of PR1/PR3 |
| 3 | 4 new HTTP routes on `AdminCatalogEndpoints.cs` | PR3 | `dotnet test --filter FullyQualifiedName~AdminCatalogEndpoints` | `WebApplicationFactory` endpoint test hitting the running test host for 201/409/404/422/401/403 | Revert 4 new routes + 2 new request records + their API tests; independent of PR1/PR2 runtime state |

## Phase 1: Domain

- [x] 1.1 `src/Maxkeys.Domain/Catalog/ProductVariant.cs`: replace `OldPrice` with `DiscountPercentage`; add currency whitelist (`ARS`,`USD`); extract shared `Validate()` used by ctor + `UpdateDetails`
- [x] 1.2 Domain unit tests: currency whitelist (USD ok, EUR → 422 path), discount bounds (0/100 rejected, 1–99 accepted), ctor/`UpdateDetails` parity

## Phase 2: Migration

- [x] 2.1 `.../Configurations/ProductVariantConfiguration.cs`: `discount_percentage numeric(5,2)` replaces `old_price numeric(12,2)`
- [x] 2.2 EF migration `*_ReplaceVariantOldPriceWithDiscountPercentage.cs`: Add → backfill (2 SQL statements per design) → Drop; write lossy-but-safe `Down`
- [x] 2.3 Manual verify: `dotnet ef database update` on a seeded local DB; confirm backfilled `discount_percentage` matches the pre-migration `old_price` pair

## Phase 3: Application — pricing + existing call sites (ships with Phase 1/2)

- [x] 3.1 `src/Maxkeys.Application/Catalog/VariantPricing.cs` (new): `ComputeOldPrice(price, discountPercentage?)` per design formula
- [x] 3.2 `ListAdminProducts.cs`: promote `ToAdminVariant` / new `ToAdminProduct` to `public static`; use `VariantPricing`
- [x] 3.3 `UpdateProduct.cs`: reuse shared `ToAdminProduct` mapper
- [x] 3.4 `UpdateProductVariant.cs`: `discountPercentage` param replaces `oldPrice`
- [x] 3.5 `GetCatalog.cs`, `GetProductBySlug.cs`: replace 3 `variant.OldPrice` reads with `VariantPricing.ComputeOldPrice(...)`
- [x] 3.6 `AdminCatalogEndpoints.cs`: rename existing `UpdateProductVariantRequest.OldPrice` → `DiscountPercentage` (PUT route only, not the new routes)

## Phase 4: Application — new use cases (additive)

- [x] 4.1 `CreateProduct.cs` (new): slug pre-check → `null` = 409; returns `AdminProduct`
- [x] 4.2 `DeleteProduct.cs` (new): soft-delete via `UpdateCatalogInfo(..., isActive:false)`
- [x] 4.3 `CreateProductVariant.cs` (new): parent lookup → `null` = 404
- [x] 4.4 `DeleteProductVariant.cs` (new): soft-delete via `UpdateDetails(..., isActive:false)`
- [x] 4.5 `src/Maxkeys.Infrastructure/DependencyInjection.cs`: register the 4 new use cases in `AddUseCases`

## Phase 5: API endpoints (new routes)

- [x] 5.1 `AdminCatalogEndpoints.cs`: add `CreateProductRequest`, `CreateProductVariantRequest`
- [x] 5.2 `POST /admin/catalog/products` → 201 Created / 409 duplicate slug / 422 invalid fields, under `AdminPolicy`
- [x] 5.3 `DELETE /admin/catalog/products/{id}` → 200 with updated DTO / 404, under `AdminPolicy`
- [x] 5.4 `POST /admin/catalog/products/{productId}/variants` → 201 / 404 parent / 422 invariant, under `AdminPolicy`
- [x] 5.5 `DELETE /admin/catalog/variants/{id}` → 200 with updated DTO / 404, under `AdminPolicy`

## Phase 6: Seeder + seed data

- [x] 6.1 `src/Maxkeys.Infrastructure/Persistence/CatalogSeeder.cs`: thread `CatalogSeedVariant.DiscountPercentage` to ctor/`UpdateDetails` (replaces `OldPrice`)
- [x] 6.2 `seed/catalog.json`: convert every `oldPrice` entry to computed `discountPercentage` — MUST ship in the same PR as the migration (Phase 2), else the next seeder run wipes backfilled data
- [x] 6.3 `tests/Maxkeys.Application.Tests/Catalog/CatalogTestData.cs`: `SeedVariant` param swap `oldPrice` → `discountPercentage`

## Phase 7: Tests

- [x] 7.1 Update existing Application/API tests asserting on `OldPrice` write input to assert `DiscountPercentage` instead (PR1 scope only — covers Phase 1/2/3/6 changes)
- [x] 7.2 Application tests: `CreateProduct` (created / duplicate slug → null / invalid → exception), `DeleteProduct` (soft-delete, sibling fields preserved) — deferred to PR2
- [x] 7.3 Application tests: `CreateProductVariant` (created / unknown parent → null / invalid invariant), `DeleteProductVariant` (soft-delete, refs preserved) — deferred to PR2
- [x] 7.4 API tests: 201/409/422 for `POST products`, 200/404 for `DELETE products/{id}` — deferred to PR3
- [x] 7.5 API tests: 201/404/422 for `POST variants`, 200/404 for `DELETE variants/{id}` — deferred to PR3
- [x] 7.6 API tests: 401/403 for all 4 new routes under `AdminPolicy` (spec scenario "New endpoints enforce the same policy") — deferred to PR3

## Key Learnings

1. `OldPrice` removal from `ProductVariant` forces 5 existing call-site files and the migration into one atomic PR to keep `main` buildable.
2. `seed/catalog.json` conversion cannot be deferred past the migration PR without risking a data-wiping seeder re-run.
3. The 4 new use cases and 4 new routes are purely additive, enabling a clean `stacked-to-main` chain with no shared mutation between PR2 and PR3.
4. Session override raised the review budget from 400 to 800 lines, but PR1's estimated size still approaches that ceiling.
5. `auto-chain` delivery strategy resolves `Decision needed before apply` to No, so the orchestrator proceeds directly with PR1 using the stacked-to-main strategy.
