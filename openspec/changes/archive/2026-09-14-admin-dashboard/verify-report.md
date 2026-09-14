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

## Slice 3: Buyers + Resend (back PR3)

> Scope: Phase 3 only (tasks 3.1-3.15, admin-buyers spec, fulfillment spec MODIFIED delta, design D1 + D4, contract table rows for `GET /admin/buyers` and `POST /admin/orders/{id}/resend-delivery`). Phases 1-2 (slices 1-2, above) carried forward unchanged. Slices 4-5 (frontend) not applied, out of scope.

**Change**: admin-dashboard
**Branch**: feat/admin-buyers (from feat/carousel-slides, from feat/admin-catalog, from origin/main) - not pushed, no PR opened
**Commits verified**: 57d29b1 (production), 665c408 (tests), 3fbfe13 (docs/sdd)
**Mode**: Standard (Strict TDD: false)
**Verified**: 2026-09-14

### Completeness (Phase 3 tasks)

| Task | Status | Evidence |
|---|---|---|
| 3.1 OutboxEventTypes.cs | Done | OrderDeliveryResendRequested constant added |
| 3.2 DeliveryEmailItems.cs | Done | Static FromOrder(order, keyCipher), decrypts assigned keys per item |
| 3.3 OrderDeliveredHandler.cs refactor | Done | Calls DeliveryEmailItems.FromOrder; removed now-unused Maxkeys.Domain.Keys using; OrderDeliveredHandlerTests.cs untouched (not in diff) and still green (3/3) |
| 3.4 RequestDeliveryResend.cs | Done | Loads order, null to 404, DomainConflictException unless Status == Delivered to 409, else inserts OrderDeliveryResendRequested outbox row, returns event id |
| 3.5 OrderDeliveryResendHandler.cs | Done | IOutboxHandler, EventType = OrderDeliveryResendRequested, loads order with Include(Items.Keys), sends via DeliveryEmailItems.FromOrder + EmailTemplates.BuyerOrderDelivered |
| 3.6 DI registration | Done | OutboxServiceCollectionExtensions.cs registers IOutboxHandler, OrderDeliveryResendHandler; Infrastructure/DependencyInjection.cs registers RequestDeliveryResend, ListBuyers |
| 3.7 BuyersDtos.cs, ListBuyers.cs | Done | Q1 groups paid orders by email + search + paging + count; Q2 loads orders/items/keys for the page's emails; AssignedKeys count only, no key code |
| 3.8 AdminBuyersEndpoints.cs | Done | GET /admin/buyers?email=&page=&pageSize=, .RequireAuthorization(AdminPolicy.Name) |
| 3.9 AdminEndpoints.cs resend route | Done | POST /{id:guid}/resend-delivery on /admin/orders group, reads sub claim, 202/404/409 |
| 3.10 Program.cs mapping | Done | app.MapAdminBuyersEndpoints(); present |
| 3.11-3.12 RequestDeliveryResendTests.cs | Done | 3 facts: no DeliveredAt/key mutation, requestedBy audit payload, 409 + no outbox row on non-Delivered |
| 3.13 OrderDeliveryResendHandlerTests.cs | Done | 1 fact: sends via RecordingEmailSender with every key code present; OrderDeliveredHandlerTests.cs (3 facts) confirmed still green |
| 3.14 ListBuyersTests.cs | Done | 4 facts: grouping + unpaid excluded, email search, pagination + total, assigned-key-count-only |
| 3.15 AdminBuyersEndpointsTests.cs + ResendDeliveryEndpointTests.cs | Done | 3 + 5 facts: 401/403/200-no-key-code-in-raw-JSON; 401/403/202/409/404 |

15/15 Phase 3 tasks complete and match code state. No unchecked tasks in scope. git diff --stat feat/carousel-slides..HEAD: 19 files changed, 909 insertions(+), 27 deletions(-) - matches apply-progress (#302) exactly.

### Build / Test Evidence

- dotnet build (whole solution): Build succeeded, 0 Warnings, 0 Errors. Exit code 0.
- dotnet test (whole solution): **223/223 passed**, 0 failed, 0 skipped. Exit code 0.
  - Maxkeys.Domain.Tests: 65/65 passed (unchanged from slice 2)
  - Maxkeys.Application.Tests: 83/83 passed (+8 slice 3: 3 RequestDeliveryResendTests + 1 OrderDeliveryResendHandlerTests + 4 ListBuyersTests)
  - Maxkeys.Api.Tests: 75/75 passed (+8 slice 3: 3 AdminBuyersEndpointsTests + 5 ResendDeliveryEndpointTests)
- dotnet test --filter "FullyQualifiedName~Buyers|FullyQualifiedName~Resend": Application 8/8, Api 8/8, all passed.

Counts match the apply-progress record (Engram #302) exactly, and match the prompt's expected 223.

### Spec Compliance Matrix

**admin-buyers spec - 4 requirements, 7 scenarios in scope**

| Requirement | Scenario | Covering test | Result |
|---|---|---|---|
| Buyer Listing Grouped By Email | Buyer with multiple orders grouped together | ListBuyersTests.Buyer_with_multiple_paid_orders_is_grouped_and_unpaid_orders_are_excluded | PASS |
| Buyer Listing Grouped By Email | Email search narrows results | ListBuyersTests.Email_search_narrows_results_to_the_matching_buyer | PASS |
| Buyer Listing Grouped By Email | Pagination bounds page size | ListBuyersTests.Pagination_bounds_page_size_and_reports_total | PASS |
| Key Exposure in Buyer View | Key code not exposed | ListBuyersTests.Order_item_exposes_only_the_assigned_key_count (count only), AdminBuyersEndpointsTests.Listing_never_exposes_a_key_code_only_the_assigned_count (asserts Assert.DoesNotContain(code, rawJson) on the raw HTTP response body) | PASS |
| Resend Delivery Email | Resend for a delivered order | ResendDeliveryEndpointTests.Resend_for_a_delivered_order_returns_202_with_an_outbox_event_id, RequestDeliveryResendTests.Resend_writes_outbox_event_with_requested_by_equal_to_acting_admin_sub, OrderDeliveryResendHandlerTests.Resend_sends_delivery_email_with_the_same_keys_via_recording_sender | PASS |
| Resend Delivery Email | Resend rejected for non-delivered order (409, no email) | RequestDeliveryResendTests.Resend_on_non_delivered_order_is_rejected_and_writes_no_outbox_row, ResendDeliveryEndpointTests.Resend_for_a_non_delivered_order_returns_409_and_sends_no_email | PASS |
| Admin Buyers Authorization | Non-admin rejected (403) | AdminBuyersEndpointsTests.Non_admin_sub_is_forbidden, ResendDeliveryEndpointTests.Non_admin_sub_is_forbidden | PASS |

Also verified beyond named scenarios: 401 unauthenticated on both routes (AdminBuyersEndpointsTests.Anonymous_request_is_rejected, ResendDeliveryEndpointTests.Anonymous_request_is_rejected); resend on unknown order returns 404 (ResendDeliveryEndpointTests.Resend_for_an_unknown_order_returns_404) - PASS.

7/7 in-scope admin-buyers scenarios fully passing with runtime evidence. No spec-scenario gaps.

**fulfillment spec (MODIFIED "One-Time Delivery Email") - 3 scenarios in scope**

| Scenario | Covering test | Result |
|---|---|---|
| Email sent once even under retry (regression on existing automatic-transition behavior, not new to this slice) | OrderDeliveredHandlerTests.Reevaluating_an_already_processed_event_sends_no_second_email (file untouched by this diff, still green) | PASS |
| Admin resend for a delivered order - same email content sent again, logged with the admin sub | Same tests as admin-buyers "Resend for a delivered order" row above, plus RequestDeliveryResendTests.Resend_on_delivered_order_does_not_alter_delivered_at_or_keys (explicitly asserts DeliveredAt and every key EncryptedCode byte content are unchanged before/after) | PASS |
| Resend rejected outside Delivered status (409, no email) | Same tests as admin-buyers "Resend rejected for non-delivered order" row above | PASS |

3/3 in-scope fulfillment-delta scenarios fully passing with runtime evidence, including the explicit "MUST NOT alter DeliveredAt or the order keys" clause. No spec-scenario gaps.

### Correctness / Design Coherence (D1, D4, contract table)

| Check | Result |
|---|---|
| Resend route/status codes match contract table (202 {outboxEventId} / 404 / 409) | PASS - Results.Accepted(value: new ResendDeliveryResponse(outboxEventId.Value)) / Results.Problem(404) / DomainConflictException maps to 409 via existing ProblemDetailsExceptionHandler |
| Buyers route matches contract table (GET /admin/buyers?email=&page=&pageSize= to 200 {items[...], page, pageSize, total}) | PASS - shape matches BuyersPage/AdminBuyer/AdminBuyerOrder/AdminBuyerOrderItem exactly |
| AdminPolicy on both new routes | PASS - /admin/buyers group and /admin/orders group (existing) both .RequireAuthorization(AdminPolicy.Name) |
| One class per use case (ADR-02) | PASS - RequestDeliveryResend, ListBuyers are separate classes; DeliveryEmailItems and OrderDeliveryResendHandler are a shared helper and an outbox handler respectively, not use cases, consistent with D1 |
| Handler registered in OutboxServiceCollectionExtensions | PASS - services.AddScoped<IOutboxHandler, OrderDeliveryResendHandler>(); present |
| OrderDeliveredHandler behavior unchanged | PASS - refactor only extracts DeliveryEmailItems.FromOrder; OrderDeliveredHandlerTests.cs is untouched by the diff (verified via git diff) and its 3 facts (including the already-Processed-is-a-no-op regression test) remain green |
| No N+1 in ListBuyers (2 queries per D4) | PASS - Q1: single GroupBy query (count + page); Q2: single Include(Items.Keys) query filtered by that page's emails via Contains; no per-buyer round trip |
| Cancellation tokens | PASS - propagated through ListBuyers.ExecuteAsync, RequestDeliveryResend.ExecuteAsync, OrderDeliveryResendHandler.HandleAsync, and both new endpoint delegates |
| Outbox payload shape matches D1 ({"orderId","requestedBy","requestedAt"}) | PASS - exact field names and order |
| DeliveredAt/keys untouched by resend | PASS - RequestDeliveryResend never writes to the order or its keys, only inserts an OutboxEvent; OrderDeliveryResendHandler only reads and sends, never persists; proven by RequestDeliveryResendTests.Resend_on_delivered_order_does_not_alter_delivered_at_or_keys |

No design deviations found that break a spec requirement.

### Security Spot-Check

1. **Buyers JSON never includes Key.Code** - PASS. AdminBuyerOrderItem (BuyersDtos.cs) has only ProductName, VariantName, Quantity, AssignedKeys (an int count via item.Keys.Count(k => k.Status == KeyStatus.Assigned)); no key-code field exists anywhere in the Buyers DTO graph. AdminBuyersEndpointsTests.Listing_never_exposes_a_key_code_only_the_assigned_count asserts the raw HTTP response body string does not contain the plaintext code, closing the gap between "field absent" and "string absent from the wire payload."
2. **Resend cannot target another buyer order beyond admin scope** - not applicable as a distinct risk: the endpoint is AdminPolicy-gated for all callers (no buyer-level scoping exists anywhere in the admin surface by design), so there is no narrower authorization boundary to bypass.
3. **Payload injection via adminSub** - WARNING, see Issues below.

### Issues

**CRITICAL**: none.

**WARNING**
1. RequestDeliveryResend.ExecuteAsync builds the outbox JSON payload via raw string interpolation: `$$"""{"orderId":"{{orderId}}","requestedBy":"{{requestedBy}}","requestedAt":"{{now:O}}"}"""`. orderId is a Guid (cannot contain a quote character, safe by construction, matching the existing AttachKeyToOrderItem/ProcessPaymentNotification convention). requestedBy is the admin sub claim - a free-form string, and this is the first outbox payload in the codebase to interpolate a non-Guid value this way. Today it is not exploitable: AdminAuthorizationHandler (src/Maxkeys.Api/Auth/AdminPolicy.cs) only succeeds when sub is an exact StringComparer.Ordinal match against the operator-configured Auth:AdminSubs allowlist (appsettings.json: "AdminSubs": [] by default), so requestedBy can only ever be one of a small, deployer-controlled set of values (Supabase sub values are UUIDs, which cannot contain a quote, backslash, or control character). However, nothing in AuthOptions/AdminPolicy enforces that admin subs are UUID-shaped or JSON-safe - if an operator ever configures (or a future SSO/identity change introduces) an admin sub containing a quote or control character, the outbox row is written with invalid JSON, and OrderDeliveryResendHandler.ParseOrderId's JsonDocument.Parse(payload) throws on every processing attempt, permanently failing that resend (and any outbox retry, since the payload never changes) with no admin-facing error (the 202 was already returned before dispatch). Recommend JsonSerializer.Serialize (or at minimum manual escaping) for requestedBy instead of raw interpolation, to remove the implicit dependency on admin-sub format staying quote-free.

**SUGGESTION**
1. OrderDeliveryResendHandler does not replicate OrderDeliveredHandler's "already Processed row is a no-op" guard. Assessed against the spec: not a defect. The admin-buyers spec Resend Delivery Email requirement explicitly states "No rate limit applies," and the fulfillment spec at-least-once/idempotency language ("Handler retries MAY duplicate the email... (at-least-once)") is scoped to the original OrderDelivered transition, not the resend path - resend has no "exactly once" requirement to protect. A duplicate send on outbox retry (handler succeeds, MarkProcessed fails) is a bounded, spec-permitted consequence identical in shape to the original delivery own at-least-once behavior, not a new risk introduced by this slice.
2. Untracked src/Maxkeys.Api/Properties/ and tests/Maxkeys.Api.Tests/Properties/ (IDE-generated launchSettings.json) remain in git status, still not covered by .gitignore (confirmed: no Properties entry). Carried forward unresolved across all 3 slice verifications now (first flagged in slice 1). Recommend resolving (gitignore or intentional commit) before PR1 opens, since PR1 is the earliest branch these could leak into; not blocking for PR3.
3. The requestedBy audit trail is proven only via RequestDeliveryResendTests.Resend_writes_outbox_event_with_requested_by_equal_to_acting_admin_sub reading the raw OutboxEvent.Payload string; there is no separate structured audit-log table or query surface - the outbox row itself is the only durable record of which admin triggered a resend. This matches design D1 stated rationale ("requestedBy in the payload is a queryable audit record with no migration") and is not a gap for this slice, just a note that the audit trail retention depends entirely on the outbox table not being purged (no purge policy found in this codebase).

### Review Budget

Authored diff vs feat/carousel-slides is 900 lines (315 production + 585 tests), 0 generated files, per apply-progress (#302) and git diff --stat (909 insertions total, minus openspec doc lines, approx 900 authored src/tests). Exceeds both the tasks.md forecast (~450) and the 800-line hard cap. Per the task instructions, size:exception has already been accepted by the user for this slice - not re-flagged as a finding here, consistent with the PR2 precedent. Root cause is legitimate full spec-scenario test coverage (8 Application + 8 Api tests, one per named scenario plus 401/404 edge cases), not scope creep.

### Verdict (Slice 3)

**PASS WITH WARNINGS.** Build succeeds with 0 warnings/errors; all 223/223 tests pass (16 new: 8 Application + 8 Api); 15/15 Phase 3 tasks complete and match the code exactly; all 7 in-scope admin-buyers spec scenarios and all 3 in-scope fulfillment-delta scenarios have passing runtime-covering tests, including the explicit "MUST NOT alter DeliveredAt or the order keys" clause; design D1/D4 and the API contract table are followed with zero deviations that break a spec requirement; OrderDeliveredHandler existing behavior and tests are unchanged and green; no N+1; cancellation tokens propagated throughout; buyers JSON never exposes a key code (verified at the raw-JSON level, not just the DTO shape). One WARNING (unescaped adminSub string interpolation into an outbox JSON payload - not exploitable today given the closed admin allowlist, but a real robustness gap worth fixing before admin-sub format assumptions change) and 3 SUGGESTION-level findings, none blocking. The previously accepted size:exception for the review-budget overage is honored and not re-litigated here.


## Slice 4: Frontend guard+catalog+carousel (front PR4, maxkeys-front repo)

> Scope: Phase 4 only (tasks 4.1-4.9, plus one coordinator-requested correction commit). Phases 1-3 (backend slices 1-3, above) carried forward unchanged. Slice 5 (frontend buyers) is not applied and is out of scope for this report.

**Change**: admin-dashboard
**Repo**: maxkeys-front (sibling repo; owns no openspec/ - backend repo is authoritative for SDD artifacts)
**Branch**: feat/admin-area (from origin/main) - not pushed, no PR opened
**Commits verified**: ce84237 (types+middleware+productImage), fa57ccf (HeroCarousel fetch), cb1b23d (admin pages/components/composables), f79878a (restore static fallback), 556a082 (add placeholder.svg asset)
**Mode**: Standard (Strict TDD: false)
**Verified**: 2026-09-14

### Completeness (Phase 4 tasks)

| Task | Status | Evidence |
|---|---|---|
| 4.1 middleware/admin.ts | Done | Runs after auth; calls GET /admin/me once via useApi(); caches result in useState boolean-or-null admin-check; no-session/401 goes to /?login=1 via REDIRECT_COOKIE_KEY; 403 goes to / |
| 4.2 pages/admin index,catalog,carousel .vue | Done | All three declare definePageMeta middleware auth, admin |
| 4.3 components/admin ProductEditor,VariantRow,SlideForm .vue | Done | Presentational; emit save/saveVariant/cancel; API calls live in useAdminCatalog/useAdminCarousel (documented deviation from literal task text, already reconciled in tasks.md itself) |
| 4.4 components/catalog/HeroCarousel.vue | Done | Fetches GET /catalog/carousel via useAsyncData; FALLBACK_SLIDES constant (clearly commented FALLBACK-ONLY) shown when the fetch resolves empty or throws, matching design D2 Data Flow exactly |
| 4.5 utils/productImage.ts | Done | catalogImages override map removed; productImageUrl() returns product.imageUrl or PLACEHOLDER_IMAGE; PLACEHOLDER_IMAGE points at /images/products/placeholder.svg, and the referenced asset now physically exists (commit 556a082), closing the gap flagged in apply-progress |
| 4.6 types/api.ts | Done | AdminMeResponse/AdminProduct/AdminVariant/UpdateProductRequest/UpdateProductVariantRequest/CarouselSlideDto/AdminCarouselSlideDto/CarouselSlideRequest all present, field-for-field match against backend DTOs (see Contract Match below) |
| 4.7 tests/adminMiddleware.spec.ts | Done | 4 tests: no-session redirect without calling the API; admin passes plus cache verified (API called once across two navigations); 403 goes to /; 401 goes to /?login=1 plus redirect cookie set. npx vitest run tests/adminMiddleware.spec.ts -> 4/4 passed |
| 4.8 tests/HeroCarousel.spec.ts | Done | 4 tests: renders fetched slide plus product link; arrow/keyboard navigation wraps; renders 3 static fallback slides on empty API response; renders the same fallback on fetch rejection. npx vitest run tests/HeroCarousel.spec.ts -> 4/4 passed |
| 4.9 tests/productImage.spec.ts | Done | 2 tests: returns imageUrl when present; falls back to PLACEHOLDER_IMAGE with no per-slug override. npx vitest run tests/productImage.spec.ts -> 2/2 passed |

9/9 Phase 4 tasks complete and match the code state. No unchecked tasks in scope. git diff --stat origin/main..HEAD: 16 files changed, 692 insertions(+), 24 deletions(-) = 716 authored lines (includes the generated placeholder.svg asset, which is not code); under the ~550 forecast rounding margin and the 800-line hard cap - no size:exception needed for this slice.

### Build / Test Evidence

- npm test (vitest run, whole repo): 18 test files, 62/62 passed, exit code 0.
- npm run typecheck (nuxi typecheck): exit code 0, no type errors.
- npm run build (nuxt build): "Build complete!", exit code 0, all admin routes prerender/compile without SSR errors.
- Focused: npx vitest run tests/adminMiddleware.spec.ts tests/HeroCarousel.spec.ts tests/productImage.spec.ts -> 10/10 passed across those 3 files.

Counts match the apply-progress record (Engram #302) exactly, and match the state.yaml expectation of 62 or more.

### Contract Match (highest-value check - no live backend was available during apply)

Every DTO in types/api.ts added by this slice was cross-checked field-for-field, including field name, casing (System.Text.Json default camelCase), and nullability, against the backend C# records in AdminCatalogDtos.cs, CarouselDtos.cs, AdminEndpoints.cs, and the route paths in AdminCatalogEndpoints.cs, AdminCarouselEndpoints.cs, CatalogEndpoints.cs, AdminEndpoints.cs.

| Frontend type / call site | Backend type / route | Result |
|---|---|---|
| AdminMeResponse sub / middleware calls GET /admin/me | AdminMeResponse(string Sub) / GET /admin/me under AdminPolicy | MATCH |
| AdminProduct | AdminProduct record: Id, Slug, Name, Platform, IsActive, ImageKey, ImageUrl, Description, Variants | MATCH |
| AdminVariant | AdminVariant record: Id, Region, Edition, Price, OldPrice, Currency, SortOrder, IsActive | MATCH |
| UpdateProductRequest via saveProduct -> PUT /admin/catalog/products/{id} | UpdateProductRequest(Name, Platform, Description, ImageKey, IsActive) / same route | MATCH |
| UpdateProductVariantRequest via saveVariant -> PUT /admin/catalog/variants/{id} | UpdateProductVariantRequest(Price, OldPrice, Currency, Region, Edition, SortOrder, IsActive) / same route | MATCH |
| CarouselSlideDto (public) / HeroCarousel -> GET /catalog/carousel | CarouselSlideSummary(Id, Title, Caption, ImageUrl, ProductSlug, SortOrder) / same route, anonymous | MATCH |
| AdminCarouselSlideDto / useAdminCarousel -> GET /admin/carousel | AdminCarouselSlide(Id, ProductId, ProductName, ProductSlug, ProductIsActive, SortOrder, IsActive, Title, Caption, ImageKey, ImageUrl) | MATCH |
| CarouselSlideRequest / createSlide, updateSlide -> POST /admin/carousel, PUT /admin/carousel/{id} | CarouselSlideRequest(ProductId, SortOrder, IsActive, Title, Caption, ImageKey) | MATCH |
| deleteSlide -> DELETE /admin/carousel/{id} | DELETE /admin/carousel/{id} -> 204/404 | MATCH |
| ApiError (useApi.ts, pre-existing, unchanged) surfaces problem.detail/problem.title from any non-2xx body | Backend ProblemDetailsExceptionHandler maps DomainException to 422, DomainConflictException to 409, not-found to 404, all as RFC 7807 Problem Details | MATCH |

No field-name, casing, nullability, or route-path mismatches found. This is a clean contract match across all 8 new/modified DTOs and every new route consumed by this slice.

### Correctness / Design Coherence (D5, D6, contract table)

| Check | Result |
|---|---|
| Middleware behavior matches D5 | PASS - runs after auth (checked via pages/admin middleware array order auth, admin), calls /admin/me once, caches in useState admin-check, redirects non-admins to /, backend 403 stays authoritative per call |
| Unauthenticated -> login redirect | PASS - no session redirects to /?login=1 without calling the API at all |
| 403 -> home | PASS - redirects to / when the API rejects with 403 |
| 200 -> allowed, cached | PASS - API called exactly once across two navigations |
| All admin pages declare both auth and admin middleware | PASS - index.vue, catalog.vue, carousel.vue all declare both |
| PUT sends the FULL record including isActive | PASS - ProductEditor.submit and VariantRow.submit both build the complete request object; untouched fields pass through from props unchanged, matching design D3 re-send pattern |
| 422 Problem Details surfaced to the user | PASS - saveError is set in both composables catch blocks and rendered with role=alert on both catalog.vue and carousel.vue |
| Carousel product picker uses the admin product list | PASS - useAdminCarousel loads the admin product list and SlideForm select iterates it |
| Overrides optional, empty is omitted per backend DTO | PASS - ProductEditor/SlideForm send empty strings as undefined, which JSON.stringify drops, matching the backend nullable optional properties |
| HeroCarousel fetches GET /catalog/carousel, renders API slides, static fallback on empty/error | PASS - matches design D2 Data Flow verbatim; both fallback triggers covered by runtime tests |
| productImage.ts has no per-slug override map, placeholder asset exists | PASS - map removed; placeholder.svg physically exists, resolving the gap flagged as open in apply-progress |
| No new domain/API-shape decisions made client-side | PASS - all admin editing logic is thin composables over the existing useApi/ApiError primitives, pre-existing and unchanged in this diff |

No design deviations found that diverge from the design.md contract table or Data Flow section. The one documented deviation (components emit events instead of calling useApi directly) is presentational-layer only, already reflected in tasks.md own task text, and does not change any wire-level behavior - not re-flagged as a finding.

### Quality Spot-Checks

- Secrets: none found in the diff; useApi bearer-token attachment is pre-existing and unchanged.
- any-type creep: none found via a diff grep for any-type patterns. All new composables/components are fully typed against types/api.ts.
- Accessibility basics on forms: every input in ProductEditor, VariantRow, SlideForm is wrapped in a label with adjacent descriptive text; the one icon-only input uses a screen-reader-only span inside the label. saveError is rendered with role=alert. No unlabeled form control found.
- Loading/error states: both admin pages render a Skeleton while pending, an ErrorState with retry on error, and an EmptyState when the collection is empty, consistent with the rest of the app data-fetching pattern.
- Spanish UI copy: all new admin-page copy is Spanish, consistent with the site existing es language and the rest of the storefront - not a finding.

### Issues (Slice 4)

CRITICAL: none.

WARNING: none.

SUGGESTION
1. tests/adminMiddleware.spec.ts verifies the API-call-once cache behavior only for the cached-true (admin) path. There is no equivalent test asserting a cached-false (non-admin) result also skips a second /admin/me call on a subsequent navigation. Low risk - the code path is structurally identical to the tested true branch - but a nice-to-have for full symmetry.
2. carousel.vue delete button calls deleteSlide with no confirmation step. Not a spec or design requirement (the wire-level contract is met), but a common admin-UX safety nicety worth considering.
3. Carried forward from slices 1-3 (backend repo only, not part of this frontend slice diff): untracked src/Maxkeys.Api/Properties/ and tests/Maxkeys.Api.Tests/Properties/ remain in the backend repo git status, still unresolved. Noted for completeness; does not affect the frontend PR4 diff.

### Review Budget

Authored diff vs origin/main is 716 lines (692 insertions + 24 deletions across 16 files, including the generated placeholder.svg asset). Under the ~550-line forecast rounding margin and well under the 800-line hard cap - no size:exception needed for this slice, unlike slices 2 and 3.

### Verdict (Slice 4)

PASS. All 62/62 frontend tests pass (10 new/updated across adminMiddleware.spec.ts, HeroCarousel.spec.ts, productImage.spec.ts), typecheck is clean (exit 0), and nuxt build succeeds with all admin routes compiling/prerendering without SSR errors. 9/9 Phase 4 tasks are complete and match the code exactly. Every DTO and route added by this slice was cross-checked field-for-field against the backend actual C# records and endpoint routes with zero mismatches found. Design D5 and D6 are followed with zero deviations; the middleware redirect rules are all covered by passing runtime tests. HeroCarousel fallback behavior matches design D2 Data Flow exactly, with both empty-response and fetch-error triggers covered. The two gaps flagged as open in apply-progress (fallback removed, placeholder asset missing) were both resolved before this verification and are confirmed fixed in the final diff. 0 CRITICAL, 0 WARNING, 3 SUGGESTION findings, none blocking. No size:exception needed.


## Slice 5: Frontend buyers page + resend (front PR5, maxkeys-front repo) — FINAL SLICE

> Scope: Phase 5 only (tasks 5.1-5.4, admin-buyers spec consumption, design D4 contract table). Phases 1-4 (above) carried forward unchanged. This is the final slice of admin-dashboard.

**Change**: admin-dashboard
**Repo**: maxkeys-front (sibling repo; owns no openspec/ — backend repo is authoritative for SDD artifacts)
**Branch**: feat/admin-buyers-ui (from feat/admin-area) — not pushed, no PR opened
**Commit verified**: 0497596 `feat(admin): add buyers page with search, pagination, and delivery resend`
**Mode**: Standard (Strict TDD: false)
**Verified**: 2026-09-14

### Completeness (Phase 5 tasks)

| Task | Status | Evidence |
|---|---|---|
| 5.1 `pages/admin/buyers.vue` | Done | `definePageMeta({ middleware: ['auth','admin'] })`; search form bound to `searchTerm`; pending/error/empty states (`Skeleton`/`ErrorState`/`EmptyState`); `BuyerCard` list; Prev/Next pagination |
| 5.2 `components/admin/BuyerCard.vue` | Done | Presentational; buyer email + order count header; per-order id/status/total + items (productName/variantName/quantity/assignedKeys count only); resend button only on `Delivered`; inline confirm (no `window.confirm`) |
| 5.3 `types/api.ts` | Done | `AdminBuyerOrderItem`/`AdminBuyerOrder`/`AdminBuyer`/`AdminBuyersPage`/`ResendDeliveryResponse`, all field-for-field match backend DTOs (see Contract Match below) |
| 5.4 Tests | Done (documented deviation) | `tests/useAdminBuyers.spec.ts` (4) + `tests/adminBuyersPage.spec.ts` (1) instead of the tasks.md-literal `pages/admin/buyers.spec.ts` — matches repo convention (all specs under `tests/`), same pattern as `adminMiddleware.spec.ts`/`HeroCarousel.spec.ts` |

4/4 Phase 5 tasks complete and match code state. No unchecked tasks in scope. `git diff --stat feat/admin-area..HEAD`: 7 files changed, 391 insertions(+), 3 deletions(-) = 394 authored lines, matches apply-progress (#302) exactly.

### Build / Test Evidence (re-run this verify pass)

- `npm test` (vitest run, whole repo): 20 test files, 67/67 passed, exit 0.
- `npm run typecheck` (`nuxi typecheck`): exit 0, no type errors.
- `npm run build` (`nuxt build`): "Build complete!", exit 0; `buyers-jpMIT3s7.mjs` chunk present in `.output/server/chunks/build`.

Counts match apply-progress (#302) exactly (62 baseline + 5 new = 67).

### Contract Match (backend cross-check, re-verified this pass against current `feat/admin-buyers` branch source)

| Frontend type / call site | Backend type / route | Result |
|---|---|---|
| `AdminBuyerOrderItem {productName,variantName,quantity,assignedKeys}` | `AdminBuyerOrderItem(ProductName,VariantName,Quantity,AssignedKeys)` (camelCase on the wire) | MATCH |
| `AdminBuyerOrder {id,status,paidAt?,totalAmount,currency,items}` | `AdminBuyerOrder(Id,Status,PaidAt,TotalAmount,Currency,Items)` | MATCH |
| `AdminBuyer {email,orderCount,lastPaidAt?,orders}` | `AdminBuyer(Email,OrderCount,LastPaidAt,Orders)` | MATCH |
| `AdminBuyersPage {items,page,pageSize,total}` | `BuyersPage(Items,Page,PageSize,Total)` | MATCH |
| `ResendDeliveryResponse {outboxEventId}` | `ResendDeliveryResponse(OutboxEventId)` | MATCH |
| `OrderStatus` union (Pending/Paid/AwaitingFulfillment/Delivered/Cancelled) | `OrderStatus` enum, identical 5 values, same order | MATCH |
| `useAdminBuyers` query `{email,page,pageSize}` to `GET /admin/buyers` | `AdminBuyersEndpoints.cs`: `MapGet(string? email, int? page, int? pageSize, ...)` | MATCH — exact query-param names, defaults (`page ?? 1`, `pageSize ?? 20`) handled server-side |
| `resendDelivery()` to `POST /admin/orders/{id}/resend-delivery` | `AdminEndpoints.cs`: `group.MapPost("/{id:guid}/resend-delivery", ...)` on `/admin/orders` group | MATCH |
| 202/404/409 status handling | `Results.Accepted(...)` / `Results.Problem(404)` / `DomainConflictException` to 409 via existing `ProblemDetailsExceptionHandler` | MATCH — `ApiError.detail` surfaced by `BuyerCard.vue` on any non-2xx, including 409 (tested) |

Zero mismatches found across all 5 new/modified DTOs, the query-param names, and both consumed routes.

### Behavior Checks

| Check | Result |
|---|---|
| Page middleware `['auth','admin']` | PASS — `pages/admin/buyers.vue` `definePageMeta` |
| Search wired to `GET /admin/buyers?email=` | PASS — `onSearch()` calls `search(searchTerm.value)`, which resets `page` to 1 and reloads |
| Pagination wired (page/pageSize/total) | PASS — Prev/Next buttons call `goToPage(page +/- 1)`, disabled at bounds via `totalPages` computed |
| Resend offered only for `Delivered` orders | PASS — `BuyerCard.vue`: `v-if="order.status === 'Delivered'"` |
| Inline confirm, no `window.confirm` | PASS — `confirmingOrderId` ref gates a two-button Confirmar/Cancelar row; no `window.confirm`/`confirm(` found in the diff |
| Success/error feedback on resend | PASS — `resendSuccess` renders "Email reenviado.", `resendError` renders `role="alert"` with detail or title |
| No key code rendering anywhere | PASS — `AdminBuyerOrderItem` DTO has no key-code field at all on either side of the wire (only `assignedKeys` count); grep of `components/admin` and `pages/admin` for key/code patterns found only a source comment reaffirming intent, no rendered value |
| Buyers nav link enabled | PASS — `pages/admin/index.vue` "Compradores" `NuxtLink` to `/admin/buyers` (previously disabled per apply-progress; now live) |
| Per-order resend state isolation | PASS (structural) — `resending`/`resendError`/`resendSuccess` are `Record<string,...>` keyed by `orderId`, owned by the single `useAdminBuyers()` instance and passed down to every `BuyerCard`, so concurrent resends on different orders do not share state (see SUGGESTION 1 below — not directly exercised by a two-order-concurrent test) |

No design (D4) or spec (admin-buyers) deviations found.

### Spec Compliance Note

The admin-buyers and fulfillment-delta spec scenarios are backend-owned and were already proven with runtime test evidence in Slice 3 above (Engram #308). This slice is the frontend consumer of that contract; its own tests prove correct consumption — grouped orders under one buyer, key-count-only rendering, resend gated on `Delivered`, and both the resend-success and resend-409 UI paths (`tests/adminBuyersPage.spec.ts`, `tests/useAdminBuyers.spec.ts`) — not a re-proof of backend behavior. No live backend was reachable in this environment during this verify pass; frontend-side runtime evidence is Vitest component/composable tests plus a clean production build, the same evidence pattern already accepted for slice 4.

### Issues (Slice 5)

CRITICAL: none.

WARNING: none.

SUGGESTION
1. No dedicated test asserts that resending order A does not affect order B's resend state when both are `Delivered` and rendered simultaneously in the same buyer card list. The `Record<string,...>`-keyed state design makes this structurally sound, but it is not directly exercised.
2. Carried forward, unresolved (backend repo only, not part of this frontend diff): untracked `src/Maxkeys.Api/Properties/` and `tests/Maxkeys.Api.Tests/Properties/` (IDE-generated launchSettings.json) remain in the backend repo git status — first flagged in slice 1, still open.
3. Resend has no client-side rate limiting or debounce beyond the one-shot confirm step — matches the design's explicitly accepted "no rate limit" decision (Open Questions), not a gap.

### Review Budget

Authored diff vs `feat/admin-area` is 394 lines (391 insertions + 3 deletions across 7 files), matching apply-progress exactly. Over the ~250-line tasks.md forecast but well under the 800-line hard cap — no `size:exception` needed for this slice.

### Verdict (Slice 5)

PASS. All 67/67 frontend tests pass (5 new: 4 `useAdminBuyers.spec.ts` + 1 `adminBuyersPage.spec.ts`), typecheck is clean (exit 0), and `nuxt build` succeeds with the buyers page chunk present in server output. 4/4 Phase 5 tasks are complete and match the code exactly. Every DTO, query-param name, and route consumed by this slice was cross-checked field-for-field against the actual current backend C# records/routes (`RequestDeliveryResend.cs`, `BuyersDtos.cs`, `AdminBuyersEndpoints.cs`, `AdminEndpoints.cs`) with zero mismatches. Design D4's contract table is followed exactly. Resend is correctly gated on `Delivered` status, uses an inline confirm (no browser dialog), surfaces success/error feedback, and never renders a key code anywhere in the buyers UI. The buyers nav link is enabled. 0 CRITICAL, 0 WARNING, 3 SUGGESTION findings, none blocking.

## Final Overall Status (Slices 1-5) — COMPLETE

All 5 planned slices across both repos are now applied and verified.

| Slice | Repo | Scope | Tests | Verdict |
|---|---|---|---|---|
| 1 | back | Admin catalog | 174/174 (remediated) | PASS |
| 2 | back | Carousel | 207/207 | PASS |
| 3 | back | Buyers + resend | 223/223 | PASS WITH WARNINGS, now PASS (WARNING resolved, see below) |
| 4 | front | Guard + catalog + carousel | 62/62, typecheck clean, build green | PASS |
| 5 | front | Buyers page + resend UI | 67/67, typecheck clean, build green | PASS |

**Total tests**: backend 223/223 (`dotnet test`, whole solution: Domain 65, Application 83, Api 75), frontend 67/67 (`vitest`, whole repo, 20 files). Combined **290/290** passing across both repos, 0 failures.

**Slice 3 WARNING resolution (verified this pass)**: the previously open WARNING — unescaped `adminSub` string interpolation into the `OrderDeliveryResendRequested` outbox JSON payload — is CONFIRMED FIXED in backend commit `d522915` "fix(fulfillment): serialize resend outbox payload with JsonSerializer" on `feat/admin-buyers`. `RequestDeliveryResend.cs` line 50 now reads `JsonSerializer.Serialize(new { orderId, requestedBy, requestedAt = now })` in place of the prior raw string interpolation. Re-verified by direct source read during this pass. This closes the only WARNING open across all 5 slices.

**Open findings by severity, across all 5 slices (final)**:
- CRITICAL: 0
- WARNING: 0 (the one WARNING, slice 3 payload escaping, is now resolved and confirmed)
- SUGGESTION: 12 total across all slices, none blocking. The recurring backend `Properties/` launchSettings.json hygiene item (flagged once per slice report since slice 1) is one open item, not 5 independent issues.

**Deploy-order note (design D7, confirmed unchanged)**: backend slices must deploy in order #1 (admin catalog) then #2 (carousel) then #3 (buyers+resend) before frontend PR5 (buyers UI) has a reachable backend to call; frontend PR4 (guard+catalog+carousel) only needs backend #1+#2 live. Frontend PR4 is safe to deploy ahead of a fully-deployed backend specifically because (a) `HeroCarousel.vue` falls back to static `FALLBACK_SLIDES` on an empty response or fetch error (verified slice 4), and (b) the admin guard middleware fails closed — `GET /admin/me` returning 403/401/network-error all redirect away from admin pages rather than expose a broken admin UI. PR5 (buyers) has no equivalent fallback — `buyers.vue`'s `ErrorState` renders on any load failure, including "backend not yet deployed" — so PR5 specifically should not be exposed to real admin users until backend PR3 is actually live, even though the PR5 frontend build itself is safe to ship as code.

**Recommendation**:
1. Proceed to `sdd-archive` for all 5 slices — no CRITICAL or WARNING issues remain anywhere in the change.
2. Before archiving, resolve or explicitly accept the one recurring SUGGESTION: untracked `src/Maxkeys.Api/Properties/` and `tests/Maxkeys.Api.Tests/Properties/` in the backend repo (confirm `.gitignore` coverage or intentional exclusion) so these IDE-generated files do not leak into PR1's diff.
3. The already-accepted `size:exception` decisions for slices 2 (1216 authored lines) and 3 (900 authored lines) remain in effect and are not re-litigated.
4. Push and open PRs 1-5 in stacked order per design D7; deploy backend #1 then #2 then #3 before exposing frontend PR5 to real admin traffic (frontend PR4 can ship once backend #1+#2 are live, thanks to the carousel fallback and fail-closed guard).
