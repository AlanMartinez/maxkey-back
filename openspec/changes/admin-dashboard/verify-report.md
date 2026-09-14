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

## Slice 2: Carousel (back PR2)

> Scope: Phase 2 only (tasks 2.1-2.11, carousel spec, design D2, contract table rows for `/admin/carousel` and `GET /catalog/carousel`). Phase 1 (slice 1) is carried forward unchanged from the section above. Slices 3-5 (buyers/resend, frontend) are not applied and are out of scope for this report.

**Change**: admin-dashboard
**Branch**: feat/carousel-slides (from feat/admin-catalog, from origin/main) — not pushed, no PR opened
**Commits verified**: e262404 (domain), 23d5b28 (EF/migration), dd257f1 (application), 08ea310 (api), 66de893 (docs/sdd)
**Mode**: Standard (Strict TDD: false)
**Verified**: 2026-09-14

### Completeness (Phase 2 tasks)

| Task | Status | Evidence |
|---|---|---|
| 2.1 `CarouselSlide.cs` | Done | `Entity` subclass; ctor + `Update` share invariants (`ProductId != Guid.Empty`, `SortOrder >= 0`); `Normalize()` blanks-to-null, trims |
| 2.2 `IAppDbContext`/`AppDbContext` | Done | `DbSet<CarouselSlide> CarouselSlides` added to both |
| 2.3 `CarouselSlideConfiguration.cs` | Done | Maxlengths 200/500/500; `HasOne<Product>().WithMany().OnDelete(Restrict)`; composite index `(IsActive, SortOrder)` |
| 2.4 Migration `AddCarouselSlides` | Done | Creates `carousel_slides` + FK `fk_carousel_slides_products_product_id` (Restrict) + 2 indexes; `Down()` drops the table; snapshot delta is carousel-only, no unrelated drift |
| 2.5 Carousel DTOs + use cases | Done | `CarouselDtos.cs`, `GetCarousel`, `ListCarouselSlides`, `CreateCarouselSlide`, `UpdateCarouselSlide`, `DeleteCarouselSlide` all present and match D2 |
| 2.6 `AdminCarouselEndpoints.cs` | Done | `/admin/carousel` group, all routes under `.RequireAuthorization(AdminPolicy.Name)`; GET/POST/PUT/DELETE |
| 2.7 `CatalogEndpoints.cs` public route | Done | `GET /catalog/carousel` added inside existing anonymous `/catalog` group |
| 2.8 `Program.cs` mapping | Done | `app.MapAdminCarouselEndpoints();` present |
| 2.9 Domain tests | Done | `CarouselSlideTests.cs` — 9 tests, all invariants covered |
| 2.10 Application tests | Done | `GetCarouselTests` (5), `ListCarouselSlidesTests` (1), `CreateCarouselSlideTests` (2), `UpdateCarouselSlideTests` (3), `DeleteCarouselSlideTests` (2) = 13 tests |
| 2.11 Api tests | Done | `AdminCarouselEndpointsTests.cs` (9), `CarouselEndpointTests.cs` (2) = 11 tests |

11/11 Phase 2 tasks complete and match code state. No unchecked tasks in scope.

### Build / Test Evidence

- `dotnet build` (whole solution): Build succeeded, 0 Warnings, 0 Errors. Exit code 0.
- `dotnet test` (whole solution): **207/207 passed**, 0 failed, 0 skipped. Exit code 0.
  - Maxkeys.Domain.Tests: 65/65 passed (+9 carousel)
  - Maxkeys.Application.Tests: 75/75 passed (+13 carousel)
  - Maxkeys.Api.Tests: 67/67 passed (+11 carousel)

Counts match the apply-progress record (Engram #302) exactly, and match the prompt's expected 207.

### Spec Compliance Matrix (carousel spec) — 4 requirements, 7 scenarios in scope

| Requirement | Scenario | Covering test | Result |
|---|---|---|---|
| Admin Slide Creation | Slide created for an existing product | `CreateCarouselSlideTests.Creates_slide_for_an_existing_product`, `AdminCarouselEndpointsTests.Admin_can_create_a_slide_for_an_existing_product` | PASS |
| Admin Slide Creation | Slide creation rejects unknown product (422) | `CreateCarouselSlideTests.Rejects_unknown_product_with_domain_exception`, `AdminCarouselEndpointsTests.Create_with_unknown_product_returns_422` (asserts `application/problem+json`) | PASS |
| Admin Slide Update and Removal | Slide reordered | `UpdateCarouselSlideTests.Reorders_a_slide`, `AdminCarouselEndpointsTests.Admin_can_reorder_a_slide`, `GetCarouselTests.Orders_by_sort_order_ascending` | PASS |
| Admin Slide Update and Removal | Slide deleted | `DeleteCarouselSlideTests.Deletes_an_existing_slide`, `AdminCarouselEndpointsTests.Admin_can_delete_a_slide` (asserts absence from admin listing) | PASS |
| Public Carousel Listing | Inactive slide hidden | `GetCarouselTests.Hides_inactive_slide`, `CarouselEndpointTests.Hides_inactive_slides_and_slides_for_inactive_products` | PASS |
| Public Carousel Listing | Slide hidden when its product is inactive | `GetCarouselTests.Hides_slide_when_its_product_is_inactive`, `CarouselEndpointTests.Hides_inactive_slides_and_slides_for_inactive_products` | PASS |
| Public Carousel Listing | Override falls back to product fields | `GetCarouselTests.Falls_back_to_product_title_and_image_when_no_override`, `GetCarouselTests.Uses_slide_override_instead_of_product_fields` | PASS |
| Admin Carousel Authorization | Non-admin rejected (403) | `AdminCarouselEndpointsTests.Non_admin_sub_is_forbidden_from_list` | PASS |

Also verified beyond the named scenarios (implied by task list / API contract table, not named spec scenarios): unauthenticated request rejected (401) via `AdminCarouselEndpointsTests.Anonymous_request_to_list_is_rejected`; admin listing includes inactive slides and slides for inactive products via `ListCarouselSlidesTests.Includes_inactive_slides_and_slides_linked_to_inactive_products` and `AdminCarouselEndpointsTests.Admin_listing_includes_inactive_slides`; update with unknown slide id returns 404 via `UpdateCarouselSlideTests.Returns_null_for_unknown_slide_id` and `AdminCarouselEndpointsTests.Update_with_unknown_slide_id_returns_404`; delete with unknown id returns 404 via `DeleteCarouselSlideTests.Returns_false_for_unknown_id` and `AdminCarouselEndpointsTests.Delete_with_unknown_id_returns_404` — all PASS.

7/7 in-scope scenarios fully passing with runtime evidence. No spec-scenario gaps.

### Correctness / Design Coherence (D2, contract table)

| Check | Result |
|---|---|
| Entity fields/invariants match D2 | PASS. `ProductId` required (non-`Guid.Empty`), `SortOrder` optional overrides `Title`/`Caption`/`ImageKey` trimmed/nulled via `Normalize()`, `Update` mirrors ctor |
| EF config: FK to products, Restrict, snake_case table, indexes | PASS. `carousel_slides` table, `fk_carousel_slides_products_product_id` with `OnDelete(Restrict)`, composite index `(is_active, sort_order)` plus FK index `product_id` |
| Migration `AddCarouselSlides` present and applies via Testcontainers | PASS. Applied automatically by `PostgresFixture` (Application.Tests) and `WebApplicationFactory`/`ApiTestFixture`/`Hs256ApiTestFixture` (Api.Tests); real Postgres, not mocked |
| Migration `Down()` drops table; no unrelated snapshot drift | PASS. `Down()` is a single `DropTable(carousel_slides)`; `AppDbContextModelSnapshot.cs` diff is carousel-only (PK, 2 indexes, FK, table mapping) |
| Routes match design contract table | PASS. `GET/POST /admin/carousel`, `PUT/DELETE /admin/carousel/{id}`, `GET /catalog/carousel`, status codes (201/200/404/422/204) all match |
| All admin routes under `AdminPolicy` | PASS. `.RequireAuthorization(AdminPolicy.Name)` applied once at the `/admin/carousel` group level, covers all 4 routes |
| Public endpoint anonymous | PASS. `GET /catalog/carousel` added to the existing unauthenticated `/catalog` group, no auth middleware |
| One class per use case (ADR-02) | PASS. `GetCarousel`, `ListCarouselSlides`, `CreateCarouselSlide`, `UpdateCarouselSlide`, `DeleteCarouselSlide` are separate classes; `ListCarouselSlides.ToAdminCarouselSlide` is a shared internal static mapper, not a use case |
| DI registration | PASS. `Infrastructure/DependencyInjection.cs` lines 63-67 register all 5 use cases as scoped |
| N+1 check (GetCarousel/ListCarouselSlides) | PASS. Both use a single LINQ join (one SQL query via ToListAsync), no per-row product lookups |
| Cancellation tokens | PASS. Every use case method and endpoint delegate takes and forwards CancellationToken to ToListAsync/SingleOrDefaultAsync/SaveChangesAsync |
| Delete semantics | PASS. Hard delete (CarouselSlides.Remove); design does not call for soft delete, matches Slide deleted scenario wording (no longer appears) |
| SortOrder ordering/ties | PASS. Both GetCarousel and ListCarouselSlides order by SortOrder then Id, giving a stable, deterministic tie-break; spec does not require uniqueness and none is enforced (consistent with the reorder scenario, which only requires relative ordering) |
| Nullable overrides fallback | PASS. slide.Title ?? product.Name pattern in GetCarousel; AdminCarouselSlide exposes raw nullable override fields separately from the resolved ImageUrl, matching the contract table |
| Image URL resolution | PASS. ImageUrlBuilder.Build(slide.ImageKey ?? product.ImageKey) used consistently in both public and admin projections |

No design deviations found for slice 2.

### Deviation Assessment: unknown-product 422 vs null/404

Apply-progress (#302) flags that CreateCarouselSlide/UpdateCarouselSlide throw DomainException (-> 422) for an unknown productId, instead of returning null (-> 404) the way UpdateProduct/UpdateProductVariant do for an unknown primary id in slice 1.

Assessment: not a deviation, and acceptable. The design's own Interfaces/Contracts table (design.md line 72) specifies "PUT /admin/carousel/{id}" -> 200 / 404 / 422, i.e. two distinct failure modes are already designed in: 404 for an unknown slide id (the resource being edited, UpdateCarouselSlide returns null for this, mapped to 404 by the endpoint, exactly like slice 1's pattern) and 422 for an unknown productId (a foreign reference inside the body, a domain-validation failure, matching the carousel spec's explicit "Slide creation rejects unknown product... response is 422 Problem Details" wording). The implementation reproduces both codes correctly and distinguishes "editing something that does not exist" (404) from "referencing something that does not exist" (422), a more precise mapping than a blanket null/404, not a looser one. No spec or design requirement is broken; downgraded from a flagged risk to a confirmed correct implementation.

### Quality Spot-Checks

- Cancellation tokens: propagated everywhere. PASS (see Correctness table above).
- N+1: none found in GetCarousel/ListCarouselSlides. PASS.
- Delete semantics: hard delete, matches design. PASS.
- SortOrder ordering: stable tie-break by Id, no uniqueness constraint (not required). PASS.
- Nullable overrides: normalized and resolved correctly. PASS.
- Migration sanity: Down() drops table cleanly; snapshot drift is carousel-only. PASS.
- Untracked src/Maxkeys.Api/Properties/ and tests/Maxkeys.Api.Tests/Properties/: same IDE-generated artifacts flagged as a SUGGESTION in the slice 1 report; still present, still unrelated to this slice's diff (confirmed via git status, not staged, not part of the feat/admin-catalog..HEAD diff).

### Review Budget

Authored diff vs feat/admin-catalog is 1216 lines (777 test + 439 production), excluding the 606 generated migration lines (.cs + .Designer.cs + snapshot delta), per apply-progress (#302) and git diff --stat. This exceeds the 800-line hard cap forecast in tasks.md (~600 estimated). Per the task instructions, size:exception has already been accepted by the user for this slice, not re-flagged as a finding here. Root cause (full spec-scenario test coverage: 9 domain + 13 application + 11 Api tests, one per named scenario plus 404/401 edge cases) is legitimate test-volume, not scope creep; every test traces to either a named spec scenario or an explicit design contract-table status code.

### Issues (Slice 2)

No CRITICAL issues.

No WARNING issues.

SUGGESTION
1. No API-level (end-to-end through the exception handler) test exercises PUT /admin/carousel/{id} with an unknown productId returning 422; only the Application-layer UpdateCarouselSlideTests.Rejects_unknown_product_with_domain_exception proves this. The POST path's equivalent is already proven end-to-end (AdminCarouselEndpointsTests.Create_with_unknown_product_returns_422), and both use cases throw the same DomainException through the same ProblemDetailsExceptionHandler, so risk is very low. This is not a named spec scenario for PUT, only implied by the design contract table's 422 status code. Nice-to-have for full endpoint-level symmetry with the POST case.
2. Carried forward from slice 1, unresolved: untracked src/Maxkeys.Api/Properties/ and tests/Maxkeys.Api.Tests/Properties/ remain in git status. Confirm .gitignore coverage (or intentional exclusion) before PR2 is opened, so they do not leak into the diff.

### Verdict (Slice 2)

PASS. Build succeeds with 0 warnings/errors; all 207/207 tests pass (33 new carousel tests: 9 domain + 13 application + 11 Api); 11/11 Phase 2 tasks complete and match the code exactly; all 7 in-scope carousel spec scenarios have passing runtime-covering tests, no gaps; design D2 and the API contract table are followed with zero deviations (the flagged unknown-product-422-vs-404 point is confirmed to match the design's own contract table, not a deviation); migration is sound (Down() drops cleanly, no unrelated snapshot drift); no N+1; cancellation tokens propagated throughout. 2 SUGGESTION-level findings only, neither blocking. The previously accepted size:exception for the review-budget overage is honored and not re-litigated here.

## Slices 3-5

Not applied. Phase 3 (Buyers+Resend), Phase 4/5 (frontend) have 0/23 remaining tasks checked in tasks.md and no corresponding apply-progress artifact. Out of scope for this report; do not infer spec/design compliance for them from this verification. Per design D7, Phase 3 (back PR3) depends on PR1 merged; do not start until PR1 and PR2's size:exception/merge status are resolved by the maintainer.
