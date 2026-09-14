# Verify Report: admin-dashboard - Slice 1 (back PR1, Admin Catalog)

> Scope: Phase 1 only (tasks 1.1-1.10, admin-catalog spec, design D3 + D5). Slices 2-5 (carousel, buyers/resend, frontend) are not applied and are out of scope for this report.

**Change**: admin-dashboard
**Branch**: feat/admin-catalog (4 commits ahead of origin/main, not pushed, no PR opened)
**Commit verified**: b12fcbb
**Mode**: Standard (Strict TDD: false)
**Verified**: 2026-09-14

## Completeness (Phase 1 tasks)

| Task | Status | Evidence |
|---|---|---|
| 1.1 AdminCatalogDtos.cs | Done | AdminProduct/AdminVariant records present, match design D3 contract table |
| 1.2 ListAdminProducts.cs | Done | 2-query shape (products, then variants via Contains), no N+1 |
| 1.3 UpdateProduct.cs | Done | Calls Product.UpdateCatalogInfo; returns null on unknown id |
| 1.4 UpdateProductVariant.cs | Done | Calls ProductVariant.UpdateDetails; returns null on unknown id |
| 1.5 AdminCatalogEndpoints.cs | Done | GET/PUT /admin/catalog/products/{id}, PUT /admin/catalog/variants/{id}, .RequireAuthorization(AdminPolicy.Name) |
| 1.6 AdminEndpoints.cs - GET /admin/me | Done | Returns AdminMeResponse(sub) under its own AdminPolicy group |
| 1.7 Program.cs mapping | Done | app.MapAdminCatalogEndpoints(); present |
| 1.8 Docs bootstrap-only note | Done | docs/local-demo.md:40, docs/runbook-sandbox.md:22 both flag --seed-catalog as bootstrap-only/upsert-by-slug |
| 1.9 Application tests | Done | ListAdminProductsTests (1), UpdateProductTests (3), UpdateProductVariantTests (3) - all passing |
| 1.10 Api tests | Done | AdminCatalogEndpointsTests (8), AdminMeEndpointTests (3) - all passing |
## Build / Test Evidence

- dotnet build (whole solution): Build succeeded, 0 Warnings, 0 Errors. Exit code 0.
- dotnet test (whole solution): 171/171 passed, 0 failed, 0 skipped. Exit code 0.
  - Maxkeys.Domain.Tests: 56/56 passed
  - Maxkeys.Application.Tests: 61/61 passed (includes 7 new slice-1 tests: ListAdminProductsTests x1, UpdateProductTests x3, UpdateProductVariantTests x3)
  - Maxkeys.Api.Tests: 54/54 passed (includes 11 new slice-1 tests: AdminCatalogEndpointsTests x8, AdminMeEndpointTests x3)

Counts match the apply-progress record (Engram #300) exactly.

## Spec Compliance Matrix (admin-catalog spec)

| Requirement | Scenario | Covering test | Result |
|---|---|---|---|
| Admin Product Listing Including Inactive | Inactive products included | ListAdminProductsTests.Includes_inactive_products_and_inactive_variants, AdminCatalogEndpointsTests.Admin_listing_includes_inactive_products | PASS |
| Admin Product Content Update | Description and ImageKey updated | UpdateProductTests.Updates_description_and_image_key, AdminCatalogEndpointsTests.Admin_can_update_product_description_and_image_key | PASS |
| Admin Product Content Update | Invalid update rejected (422, product unchanged) | UpdateProductTests.Empty_required_field_throws_domain_exception_and_leaves_product_unchanged, AdminCatalogEndpointsTests.Empty_required_field_on_product_update_returns_422 | PASS |
| Admin Product Activation Toggle | Product deactivated (hidden from public listing) | none | CRITICAL - UNTESTED |
| Admin Variant Activation Toggle | Variant deactivated (hidden from public detail) | UpdateProductVariantTests.Updates_price_and_active_flag, AdminCatalogEndpointsTests.Admin_can_deactivate_a_variant (toggle write only, not hidden-from-public-read) | WARNING - partially tested |
| Admin Catalog Authorization | Unauthenticated request rejected (401) | AdminCatalogEndpointsTests.Anonymous_request_to_list_is_rejected, AdminMeEndpointTests.Anonymous_request_is_rejected | PASS |
| Admin Catalog Authorization | Non-admin rejected (403) | AdminCatalogEndpointsTests.Non_admin_sub_is_forbidden_from_list, AdminMeEndpointTests.Non_admin_sub_is_forbidden | PASS |

Also verified (404 unknown id, not a named spec scenario but implied by "Admin Product Content Update"/API contract): UpdateProductTests.Returns_null_for_unknown_id, UpdateProductVariantTests.Returns_null_for_unknown_id, AdminCatalogEndpointsTests.Unknown_product_id_returns_404 - PASS. Invalid_variant_price_returns_422 (422 mirror for variant) - PASS.

5 requirements, 7 scenarios total in scope. 5/7 fully passing with runtime evidence, 1 partially covered, 1 untested.
## Correctness / Design Coherence (D3, D5)

| Check | Result |
|---|---|
| Routes match API contract table | PASS - GET /admin/catalog/products, PUT /admin/catalog/products/{id}, PUT /admin/catalog/variants/{id}, GET /admin/me, all under .RequireAuthorization(AdminPolicy.Name) |
| No new domain methods (D3) | PASS - reuses Product.UpdateCatalogInfo and ProductVariant.UpdateDetails verbatim; no PATCH, no SetActive()/toggle endpoint added |
| One class per use case (ADR-02) | PASS - ListAdminProducts, UpdateProduct, UpdateProductVariant are separate classes |
| DI registration | PASS - Infrastructure/DependencyInjection.cs:59-61 registers all three use cases as scoped |
| ImageUrl via ImageUrlBuilder in admin DTOs (design contract table) | PASS - ListAdminProducts/UpdateProduct both call _imageUrlBuilder.Build(product.ImageKey) |
| 404 as Problem Details | PASS - Results.Problem(statusCode: 404, ...) |
| 422 as Problem Details (domain validation) | PASS - ProblemDetailsExceptionHandler maps DomainException to 422, verified application/problem+json content type in Empty_required_field_on_product_update_returns_422 |
| Slug immutable | PASS - UpdateProductRequest has no Slug field; UpdateCatalogInfo never touches Slug |
| Async/CancellationToken propagation | PASS - every use case method and endpoint delegate takes and forwards CancellationToken |
| No N+1 in ListAdminProducts | PASS - 2 queries total (products, then variants via productIds.Contains) |
| Docs: --seed-catalog bootstrap-only | PASS - both docs/local-demo.md and docs/runbook-sandbox.md updated |

No design deviations found for slice 1.
## Issues

### CRITICAL
1. ~~UNTESTED - "Product deactivated" scenario (Admin Product Activation Toggle).~~ **RESOLVED** in slice 1 remediation — see below.

### WARNING
1. ~~Partial coverage - "Variant deactivated" scenario (Admin Variant Activation Toggle).~~ **RESOLVED** in slice 1 remediation — see below.

### SUGGESTION
1. Untracked src/Maxkeys.Api/Properties/launchSettings.json and tests/Maxkeys.Api.Tests/Properties/launchSettings.json appear in git status but are IDE-generated local dev artifacts unrelated to this slice (not part of the diff, not referenced by any task). Confirm they are covered by .gitignore or intentionally excluded before the PR is opened, so they do not leak into the PR1 diff by accident.
2. ~~Empty_required_field_on_product_update_returns_422 (Api-level) checks status/content-type but not that the product row is unchanged~~ **RESOLVED** in slice 1 remediation — see below.

## Slice 1 remediation (this batch)

Closes the CRITICAL and WARNING findings above without touching slices 2-5.

| Finding | Resolution | New/changed test |
|---|---|---|
| CRITICAL — "Product deactivated" untested | Added an Api test that PUTs `isActive: false` on an admin-updated product and asserts it disappears from the public `GET /catalog/products` listing (end-to-end through `GetCatalog`); added an Application test asserting `UpdateProduct` persists `IsActive == false`. | `AdminCatalogEndpointsTests.Deactivating_a_product_hides_it_from_the_public_catalog_listing`, `UpdateProductTests.Deactivating_a_product_persists_is_active_false` |
| WARNING — "Variant deactivated" only partially tested | Added an Api test that PUTs `isActive: false` on a variant and asserts it is excluded from the public `GET /catalog/products/{slug}` response `Variants` list (end-to-end through `GetProductBySlug`), closing the hidden-from-public half of the THEN clause via the actual admin flow. | `AdminCatalogEndpointsTests.Deactivating_a_variant_hides_it_from_the_public_product_detail` |
| SUGGESTION #2 — 422 test didn't assert product unchanged at Api level | Extended `Empty_required_field_on_product_update_returns_422` to re-read the product via `GET /admin/catalog/products` after the failed PUT and assert `Name`/`Platform` are unchanged. | `AdminCatalogEndpointsTests.Empty_required_field_on_product_update_returns_422` (extended) |

Build/test evidence: `dotnet build` — 0 Warnings, 0 Errors. `dotnet test` (whole solution) — 174/174 passed, 0 failed (Domain 56, Application 62, Api 56; +1 Application test, +2 Api tests over the prior 171).

Updated Spec Compliance Matrix rows:

| Requirement | Scenario | Covering test | Result |
|---|---|---|---|
| Admin Product Activation Toggle | Product deactivated | `UpdateProductTests.Deactivating_a_product_persists_is_active_false`, `AdminCatalogEndpointsTests.Deactivating_a_product_hides_it_from_the_public_catalog_listing` | PASS |
| Admin Variant Activation Toggle | Variant deactivated | `UpdateProductVariantTests.Updates_price_and_active_flag`, `AdminCatalogEndpointsTests.Admin_can_deactivate_a_variant`, `AdminCatalogEndpointsTests.Deactivating_a_variant_hides_it_from_the_public_product_detail` | PASS |

7/7 in-scope scenarios now fully passing with runtime evidence. Verdict is no longer blocked by CRITICAL/WARNING findings; remaining open item is the launchSettings.json SUGGESTION (unrelated dev-artifact hygiene, not spec compliance).

## Verdict

PASS WITH WARNINGS - build and all 171 existing tests are green, 10/10 Phase 1 tasks are complete and match the code, and design/ADR conformance (D3, D5, ADR-02) holds with no deviations. However, one spec scenario ("Product deactivated") has zero test coverage and is elevated to CRITICAL per the spec-scenario-compliance rule; one more ("Variant deactivated") is only partially covered (WARNING). Recommend adding the missing product-deactivation test (and, ideally, closing the hidden-from-public loop for the variant scenario) before treating slice 1 as fully spec-compliant and opening the PR. This does not block continuing to slice 2 (Carousel) in parallel, since PR1 code, tests, and docs are otherwise sound, but the PR1 diff itself should not be merged/archived as fully verified until the CRITICAL gap is closed.

**Superseded by "Slice 1 remediation" above** — the CRITICAL and WARNING findings are now closed with runtime test evidence; 174/174 tests pass. This original verdict text is left intact as the historical record of the first verify pass. Re-running `sdd-verify` is recommended to issue a fresh formal verdict, but no known blocking gap remains for slice 1.

## Slices 2-5

Not applied. Phase 2 (Carousel), Phase 3 (Buyers+Resend), Phase 4/5 (frontend) have 0/39 tasks checked in tasks.md and no corresponding apply-progress artifact. Out of scope for this report; do not infer spec/design compliance for them from this verification.
