# Tasks: Admin Dashboard (catalog, carousel, buyers)

## Review Workload Forecast

| Field | Value |
|---|---|
| Estimated changed lines | ~400 / ~600 / ~450 / ~550 / ~250 (slices 1–5); total ~2250 |
| 800-line budget risk | Low (each slice ≤ budget; slices 2 (~600) and 4 (~550) closest — flagged) |
| Chained PRs recommended | Yes |
| Chain strategy | stacked-to-main (already decided, design D7) |
| Delivery strategy | ask-on-risk |
| Decision needed before apply | No — chain strategy pre-selected, no slice exceeds 800 |

```text
Decision needed before apply: No
Chained PRs recommended: Yes
Chain strategy: stacked-to-main
800-line budget risk: Low
```

### Suggested Work Units

| Unit | Goal | PR | Focused test | Runtime harness | Rollback boundary |
|---|---|---|---|---|---|
| 1 | Admin catalog list/update/toggle | back PR1 | `dotnet test --filter Admin` | `dotnet run` + `GET /admin/catalog/products` w/ admin JWT | `Endpoints/AdminCatalogEndpoints.cs` + use cases, reverts alone |
| 2 | CarouselSlide + CRUD + public read | back PR2 | `dotnet test --filter Carousel` | `dotnet run` + `GET /catalog/carousel` | migration `Down` drops table; new files only |
| 3 | Buyers list + resend | back PR3 | `dotnet test --filter "Buyers\|Resend"` | outbox processor run against seeded `Delivered` order | new files + `OrderDeliveredHandler` refactor (revertable) |
| 4 | Front guard+catalog+carousel | front PR4 | `npx vitest run pages/admin` | `npm run dev`, load `/admin/catalog` | new pages/middleware; `HeroCarousel.vue` fallback keeps storefront safe |
| 5 | Front buyers + resend action | front PR5 | `npx vitest run pages/admin/buyers.spec.ts` | `npm run dev`, resend button on seeded order | `pages/admin/buyers.vue` + `BuyerCard.vue` only |

## Phase 1: Admin Catalog (back, PR 1)

- [x] 1.1 `src/Maxkeys.Application/Catalog/AdminCatalogDtos.cs` — `AdminProduct`/`AdminVariant` DTOs.
- [x] 1.2 `src/Maxkeys.Application/Catalog/ListAdminProducts.cs` — all products incl. inactive + variants (2 queries).
- [x] 1.3 `src/Maxkeys.Application/Catalog/UpdateProduct.cs` → calls `Product.UpdateCatalogInfo`; 404 unknown id.
- [x] 1.4 `src/Maxkeys.Application/Catalog/UpdateProductVariant.cs` → calls `ProductVariant.UpdateDetails`; 404 unknown id.
- [x] 1.5 `src/Maxkeys.Api/Endpoints/AdminCatalogEndpoints.cs` — `GET/PUT /admin/catalog/products/{id}`, `PUT /admin/catalog/variants/{id}`, `.RequireAuthorization(AdminPolicy.Name)`.
- [x] 1.6 `src/Maxkeys.Api/Endpoints/AdminEndpoints.cs` — add `GET /admin/me` returning `{sub}`.
- [x] 1.7 `Program.cs` — map `AdminCatalogEndpoints`.
- [x] 1.8 Document `--seed-catalog` as bootstrap-only (upserts by slug, overwrites admin edits) in `docs/local-demo.md` and `docs/runbook-sandbox.md`.
- [x] 1.9 Tests: `tests/Maxkeys.Application.Tests/Catalog/{ListAdminProductsTests,UpdateProductTests,UpdateProductVariantTests}.cs` — inactive-included, 422 on empty field, 404 unknown id. Verify: `dotnet test --filter Admin`.
- [x] 1.10 Tests: `tests/Maxkeys.Api.Tests/Admin/{AdminCatalogEndpointsTests,AdminMeEndpointTests}.cs` — 401/403/200 via `Hs256ApiTestFixture.AdminSub`. Verify: `dotnet test --filter Admin`.

## Phase 2: Carousel (back, PR 2 — depends on PR 1 merged)

- [x] 2.1 `src/Maxkeys.Domain/Carousel/CarouselSlide.cs` — ctor/`Update` invariants (`ProductId != Guid.Empty`, `SortOrder >= 0`, trimmed overrides).
- [x] 2.2 `src/Maxkeys.Application/Persistence/IAppDbContext.cs`, `Infrastructure/Persistence/AppDbContext.cs` — add `DbSet<CarouselSlide>`.
- [x] 2.3 `src/Maxkeys.Infrastructure/Persistence/Configurations/CarouselSlideConfiguration.cs` — `carousel_slides`, FK Restrict, index `(is_active, sort_order)`.
- [x] 2.4 Generate EF migration `AddCarouselSlides` (+Designer, snapshot). **Excluded from authored line count; flag as generated in PR body.**
- [x] 2.5 `src/Maxkeys.Application/Carousel/CarouselDtos.cs`, `GetCarousel.cs` (public, join+filter inactive), `ListCarouselSlides.cs`, `CreateCarouselSlide.cs` (422 unknown product), `UpdateCarouselSlide.cs`, `DeleteCarouselSlide.cs`.
- [x] 2.6 `src/Maxkeys.Api/Endpoints/AdminCarouselEndpoints.cs` — admin CRUD, `.RequireAuthorization(AdminPolicy.Name)`.
- [x] 2.7 `src/Maxkeys.Api/Endpoints/CatalogEndpoints.cs` — add public `GET /catalog/carousel`.
- [x] 2.8 `Program.cs` — map `AdminCarouselEndpoints`.
- [x] 2.9 Tests: `tests/Maxkeys.Domain.Tests/Carousel/CarouselSlideTests.cs` — invariants. Verify: `dotnet test --filter Carousel`.
- [x] 2.10 Tests: `tests/Maxkeys.Application.Tests/Carousel/*Tests.cs` — hides inactive slide/product, override-vs-product image resolution, reorder, delete. Verify: `dotnet test --filter Carousel`.
- [x] 2.11 Tests: `tests/Maxkeys.Api.Tests/Admin/AdminCarouselEndpointsTests.cs` (401/403/200/422) + `tests/Maxkeys.Api.Tests/Catalog/CarouselEndpointTests.cs` (anonymous, ordering). Verify: `dotnet test --filter Carousel`.

> **Slice 2 budget note (apply phase, 2026-09-14):** actual authored diff vs `feat/admin-catalog` is ~1216 lines (777 tests + 439 production), excluding the generated migration (606 lines: `.cs`+`.Designer.cs`+snapshot). This exceeds the 800-line hard cap forecast in the Review Workload Forecast (~600 estimated) — driven by full spec-scenario test coverage (9 domain + 13 application + 11 Api tests) per the Testing Strategy table. Flagged for an explicit `size:exception` decision before PR2 is opened; not split further because CarouselSlide (domain+EF+migration+CRUD+public read) is one cohesive, independently-revertable unit per design D2/D7.

## Phase 3: Buyers + Resend (back, PR 3 — depends on PR 1 merged)

- [x] 3.1 `src/Maxkeys.Domain/Outbox/OutboxEventTypes.cs` — add `OrderDeliveryResendRequested`.
- [x] 3.2 `src/Maxkeys.Application/Outbox/DeliveryEmailItems.cs` — extract `FromOrder(order, keyCipher)` shared item builder.
- [x] 3.3 `src/Maxkeys.Application/Outbox/OrderDeliveredHandler.cs` — refactor to call `DeliveryEmailItems.FromOrder`; no behavior change.
- [x] 3.4 `src/Maxkeys.Application/Fulfillment/RequestDeliveryResend.cs` — 409 unless `Status=Delivered`; writes `OrderDeliveryResendRequested{orderId, requestedBy, requestedAt}`; does not touch `DeliveredAt`/keys.
- [x] 3.5 `src/Maxkeys.Application/Outbox/OrderDeliveryResendHandler.cs` — sends via `DeliveryEmailItems.FromOrder` + `EmailTemplates.BuyerOrderDelivered`.
- [x] 3.6 `OutboxServiceCollectionExtensions.cs`, `Infrastructure/DependencyInjection.cs` — register handler + new use cases.
- [x] 3.7 `src/Maxkeys.Application/Buyers/BuyersDtos.cs`, `ListBuyers.cs` — grouped-by-email, Q1 paid orders + Q2 items/keys, substring search, `Skip/Take`+count; no key codes, only `assignedKeys` count.
- [x] 3.8 `src/Maxkeys.Api/Endpoints/AdminBuyersEndpoints.cs` — `GET /admin/buyers`, `.RequireAuthorization(AdminPolicy.Name)`.
- [x] 3.9 `src/Maxkeys.Api/Endpoints/AdminEndpoints.cs` — add `POST /admin/orders/{id}/resend-delivery` (adminSub from `sub` claim), 202/404/409.
- [x] 3.10 `Program.cs` — map `AdminBuyersEndpoints`.
- [x] 3.11 Test: `tests/Maxkeys.Application.Tests/Fulfillment/RequestDeliveryResendTests.cs` — resend does NOT alter `DeliveredAt` or the order's keys; 409 on non-`Delivered`. Verify: `dotnet test --filter Resend`.
- [x] 3.12 Test: same file — `OrderDeliveryResendRequested` outbox payload contains `requestedBy` equal to the acting `adminSub`. Verify: `dotnet test --filter Resend`.
- [x] 3.13 Test: `tests/Maxkeys.Application.Tests/Outbox/OrderDeliveryResendHandlerTests.cs` — sends via `RecordingEmailSender`; `OrderDeliveredHandlerTests.cs` still green (unchanged behavior). Verify: `dotnet test --filter "Resend|OrderDelivered"`.
- [x] 3.14 Test: `tests/Maxkeys.Application.Tests/Buyers/ListBuyersTests.cs` — grouping, search, paging, no key code exposed. Verify: `dotnet test --filter Buyers`.
- [x] 3.15 Test: `tests/Maxkeys.Api.Tests/Admin/{AdminBuyersEndpointsTests,ResendDeliveryEndpointTests}.cs` — 401/403/200/202/409. Verify: `dotnet test --filter "Buyers|Resend"`.

> **Slice 3 budget note (apply phase, 2026-09-14):** actual authored diff vs `feat/carousel-slides` is 900 lines (315 production + 585 tests), 0 generated. This exceeds both the tasks.md forecast (~450) and the 800-line hard cap — driven by full spec-scenario test coverage (3 Application test files covering resend audit/no-mutation/409, handler send, and buyers grouping/search/paging/no-key-code, plus 2 Api test files covering 401/403/202/404/409/no-key-code-in-JSON) per the design's Testing Strategy table and the prompt's explicit mandatory-tests list. Flagged for an explicit `size:exception` decision before PR3 is opened; not split further because Buyers+Resend (D1 outbox event + shared `DeliveryEmailItems` extraction + `RequestDeliveryResend` + handler + `ListBuyers` + both endpoints) is one cohesive, independently-revertable unit per design D7 — same pattern as the slice 2 overage.

## Phase 4: Frontend guard + catalog + carousel (`maxkeys-front` repo, PR 4 — depends on back PR1+PR2 merged/deployed)

> Lives in sibling repo `maxkeys-front`; do not start until backend slices 1–2 are deployed (endpoints must exist).

- [ ] 4.1 `middleware/admin.ts` — runs after `auth`, calls `GET /admin/me` once, caches in `useState('admin-check')`, redirects non-admins.
- [ ] 4.2 `pages/admin/{index,catalog,carousel}.vue` — `definePageMeta({ middleware: ['auth','admin'] })`.
- [ ] 4.3 `components/admin/{ProductEditor,VariantRow,SlideForm}.vue` — presentational, call `useApi()`.
- [ ] 4.4 `components/catalog/HeroCarousel.vue` — fetch `GET /catalog/carousel`; keep static fallback on empty/error.
- [ ] 4.5 `utils/productImage.ts` — remove the `catalogImages` override map; return `product.imageUrl || PLACEHOLDER_IMAGE`.
- [ ] 4.6 `types/api.ts` — add `AdminProduct`/`AdminVariant`/`CarouselSlide`/`AdminCarouselSlide` DTOs mirroring design's contract table.
- [ ] 4.7 Test: `middleware/adminMiddleware.spec.ts` — redirect on 403, pass on 200. Verify: `npx vitest run middleware/adminMiddleware.spec.ts`.
- [ ] 4.8 Test: `components/catalog/HeroCarousel.spec.ts` — renders API slides, falls back on error. Verify: `npx vitest run components/catalog/HeroCarousel.spec.ts`.
- [ ] 4.9 Test: `utils/productImage.spec.ts` — placeholder path, no override map. Verify: `npx vitest run utils/productImage.spec.ts`.

## Phase 5: Frontend buyers (`maxkeys-front` repo, PR 5 — depends on back PR3 merged/deployed)

> Lives in `maxkeys-front`; do not start until backend slice 3 is deployed.

- [ ] 5.1 `pages/admin/buyers.vue` — `definePageMeta({ middleware: ['auth','admin'] })`, search + pagination.
- [ ] 5.2 `components/admin/BuyerCard.vue` — orders/items, `assignedKeys` count only, resend action per `Delivered` order.
- [ ] 5.3 `types/api.ts` — `AdminBuyer`/`AdminBuyerOrder` DTOs.
- [ ] 5.4 Test: `pages/admin/buyers.spec.ts` — renders grouped orders, resend disabled unless `Delivered`, no key codes rendered. Verify: `npx vitest run pages/admin/buyers.spec.ts`.
