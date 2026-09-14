# Design: Admin Dashboard (catalog, carousel, buyers)

> Source of truth: Engram `sdd/admin-dashboard/design`. Mirrored here for hybrid mode. Builds on `openspec/changes/mvp-marketplace/design.md` (ADR-01..18).

## Technical Approach

Additive admin surface on the existing hexagonal layout: new `/admin/*` route groups under `AdminPolicy`, one use case class per operation (ADR-02), one new aggregate (`CarouselSlide`), one new outbox event type for the resend, no new interfaces (ADR-03), no S3 SDK (ADR-12). Frontend adds a guarded `pages/admin/*` area in `maxkeys-front` (ADR-18) and switches `HeroCarousel.vue` to the public carousel endpoint.

## Architecture Decisions

| # | Decision | Choice | Rejected | Rationale |
|---|---|---|---|---|
| D1 | Resend event | New type `OutboxEventTypes.OrderDeliveryResendRequested`, payload `{"orderId","requestedBy","requestedAt"}`; new `OrderDeliveryResendHandler` sharing extracted `DeliveryEmailItems.FromOrder(order, keyCipher)` with `OrderDeliveredHandler`; use case `RequestDeliveryResend` (409 unless `Delivered`) | Re-emit `OrderDelivered`; audit column on `Order` | `OrderDelivered` means "one row per Delivered transition" (ADR-04, spec One-Time Delivery Email); reusing it blurs audit and the handler's no-op guard. Outbox row already carries `CreatedAt`/`ProcessedAt`/`Attempts`, so `requestedBy` (adminSub) in the payload is a queryable audit record with no migration |
| D2 | `CarouselSlide` | `Domain/Carousel/CarouselSlide : Entity` — `ProductId` (required FK), `SortOrder`, `IsActive`, optional `Title`/`Caption`/`ImageKey` overrides; `Update(...)` mirrors ctor invariants; EF `carousel_slides`, FK Restrict, index `(is_active, sort_order)`; migration `AddCarouselSlides` | Freeform slides; store URL instead of key | User decision #295 (product-linked); `ImageKey` + `ImageUrlBuilder` keeps ADR-12; Restrict prevents deleting a product behind a slide |
| D3 | Admin catalog API | `GET /admin/catalog/products` (all, incl. inactive, with variants; 2 queries as `GetCatalog`), `PUT /admin/catalog/products/{id}` → `Product.UpdateCatalogInfo`, `PUT /admin/catalog/variants/{id}` → `ProductVariant.UpdateDetails`. No toggle endpoint or new domain method; UI toggles by re-sending the record with `isActive` flipped | PATCH; `SetActive()`; nested variant edits | Domain methods already take the full field set; PUT avoids the "null = clear vs unchanged" ambiguity for `imageKey`/`oldPrice`. Two admins, no concurrency need. `Slug` immutable (seeder key) |
| D4 | Buyers query | `GET /admin/buyers?email=&page=1&pageSize=20` (max 100). Q1: paid orders (`PaidAt != null`) grouped by `BuyerEmail`, optional `ToLower().Contains` filter, ordered by last `PaidAt` desc, `Skip/Take` + count. Q2: orders for the page's emails with `Items`+`Keys` included. No key codes returned, only `assignedKeys`/`quantity` | N+1 per email; exact-match only; return key codes | Two round-trips, bounded by page; substring search matches `GetCatalog`'s `q`. Least privilege: admins already loaded the codes |
| D5 | Frontend admin guard | `GET /admin/me` → `{sub}` (200 under `AdminPolicy`). `middleware/admin.ts` runs after `auth`, calls it once, caches in `useState('admin-check')`, redirects non-admins to `/`. API 403 stays authoritative | Expose `AdminSubs` in runtime config; rely on page-fetch 403 | Config duplication leaks the allowlist and drifts; page-level 403 renders a shell then fails. `/admin/me` is ~10 lines |
| D6 | Image override map | Keep `utils/productImage.ts` export, replace the map with `product.imageUrl \|\| PLACEHOLDER_IMAGE` | Delete file and touch every caller | Callers (`ProductCard.vue`, `cartLine.ts`) stay untouched; placeholder covers products whose R2 key is missing |
| D7 | Slicing | 5 stacked PRs to `main`, per repo, backend first (table below) | Single PR; feature-branch chain | Slices are independent; each reverts alone; all under the 800-line budget |

## Data Flow

Resend (D1; complex flow per `config.yaml` design rules):

    Admin UI ─POST /admin/orders/{id}/resend-delivery─▶ AdminEndpoints
      │                                                     │ adminSub from `sub`
      │                                        RequestDeliveryResend.ExecuteAsync
      │                                                     │ load Order; Status != Delivered → 409
      │                                                     │ OutboxEvents.Add(OrderDeliveryResendRequested)
      │◀── 202 {outboxEventId} ────────────────── SaveChangesAsync
                                                             ▼
    OutboxProcessor ─claim─▶ OrderDeliveryResendHandler ─▶ DeliveryEmailItems.FromOrder ─▶ EmailTemplates.BuyerOrderDelivered ─▶ IEmailSender

Carousel read: `HeroCarousel.vue` → `GET /catalog/carousel` → `GetCarousel` (join slides×products, `slide.IsActive && product.IsActive`, order `SortOrder, Id`) → `ImageUrlBuilder.Build(slide.ImageKey ?? product.ImageKey)`; empty/error → static fallback slides.

## File Changes

| File | Action | Description |
|---|---|---|
| `src/Maxkeys.Domain/Carousel/CarouselSlide.cs` | Create | Entity + invariants (`ProductId != Guid.Empty`, `SortOrder >= 0`, trimmed overrides) |
| `src/Maxkeys.Domain/Outbox/OutboxEventTypes.cs` | Modify | Add `OrderDeliveryResendRequested` |
| `src/Maxkeys.Application/Persistence/IAppDbContext.cs`, `Infrastructure/Persistence/AppDbContext.cs` | Modify | `DbSet<CarouselSlide> CarouselSlides` |
| `src/Maxkeys.Infrastructure/Persistence/Configurations/CarouselSlideConfiguration.cs` | Create | Mapping (D2) |
| `src/Maxkeys.Infrastructure/Persistence/Migrations/*_AddCarouselSlides.cs` (+Designer, snapshot) | Create | Generated; excluded from authored line count |
| `src/Maxkeys.Application/Catalog/{ListAdminProducts,UpdateProduct,UpdateProductVariant}.cs`, `AdminCatalogDtos.cs` | Create | D3 use cases |
| `src/Maxkeys.Application/Carousel/{GetCarousel,ListCarouselSlides,CreateCarouselSlide,UpdateCarouselSlide,DeleteCarouselSlide}.cs`, `CarouselDtos.cs` | Create | D2 use cases |
| `src/Maxkeys.Application/Buyers/ListBuyers.cs`, `BuyersDtos.cs` | Create | D4 |
| `src/Maxkeys.Application/Fulfillment/RequestDeliveryResend.cs` | Create | D1 command |
| `src/Maxkeys.Application/Outbox/OrderDeliveryResendHandler.cs`, `DeliveryEmailItems.cs` | Create | D1 handler + shared item builder |
| `src/Maxkeys.Application/Outbox/OrderDeliveredHandler.cs` | Modify | Call `DeliveryEmailItems.FromOrder` |
| `src/Maxkeys.Application/Outbox/OutboxServiceCollectionExtensions.cs`, `Infrastructure/DependencyInjection.cs` | Modify | Register handler and use cases |
| `src/Maxkeys.Api/Endpoints/{AdminCatalogEndpoints,AdminCarouselEndpoints,AdminBuyersEndpoints}.cs` | Create | Groups `/admin/catalog`, `/admin/carousel`, `/admin/buyers`, all `.RequireAuthorization(AdminPolicy.Name)` |
| `src/Maxkeys.Api/Endpoints/AdminEndpoints.cs` | Modify | Add `GET /admin/me`, `POST /admin/orders/{id}/resend-delivery` |
| `src/Maxkeys.Api/Endpoints/CatalogEndpoints.cs`, `Program.cs` | Modify | `GET /catalog/carousel`; map new groups |
| `maxkeys-front/middleware/admin.ts` | Create | D5 |
| `maxkeys-front/pages/admin/{index,catalog,carousel,buyers}.vue` | Create | `definePageMeta({ middleware: ['auth', 'admin'] })` |
| `maxkeys-front/components/admin/{ProductEditor,VariantRow,SlideForm,BuyerCard}.vue` | Create | Presentational pieces |
| `maxkeys-front/components/catalog/HeroCarousel.vue` | Modify | Fetch `/catalog/carousel`, static fallback |
| `maxkeys-front/utils/productImage.ts`, `types/api.ts` | Modify | D6; new DTO types |

## Interfaces / Contracts

| Method + path | Auth | Request | Response |
|---|---|---|---|
| `GET /admin/me` | Admin | — | `200 {sub}` / `403` |
| `GET /admin/catalog/products` | Admin | — | `200 AdminProduct[]` `{id, slug, name, platform, isActive, imageKey, imageUrl, description, variants[{id, region, edition, price, oldPrice, currency, sortOrder, isActive}]}` |
| `PUT /admin/catalog/products/{id}` | Admin | `{name, platform, description, imageKey, isActive}` | `200 AdminProduct` / `404` / `422` |
| `PUT /admin/catalog/variants/{id}` | Admin | `{price, oldPrice, currency, region, edition, sortOrder, isActive}` | `200 AdminVariant` / `404` / `422` |
| `GET /catalog/carousel` | public | — | `200 CarouselSlide[]` `{id, title, caption, imageUrl, productSlug, sortOrder}` (title defaults to product name) |
| `GET /admin/carousel` | Admin | — | `200 AdminCarouselSlide[]` `{id, productId, productName, productSlug, productIsActive, sortOrder, isActive, title, caption, imageKey, imageUrl}` |
| `POST /admin/carousel` | Admin | `{productId, sortOrder, isActive, title?, caption?, imageKey?}` | `201` / `422` (unknown product) |
| `PUT /admin/carousel/{id}` | Admin | same body | `200` / `404` / `422` |
| `DELETE /admin/carousel/{id}` | Admin | — | `204` / `404` |
| `GET /admin/buyers?email=&page=&pageSize=` | Admin | query | `200 {items[{email, orderCount, lastPaidAt, orders[{id, status, paidAt, totalAmount, currency, items[{productName, variantName, quantity, assignedKeys}]}]}], page, pageSize, total}` |
| `POST /admin/orders/{id}/resend-delivery` | Admin | — | `202 {outboxEventId}` / `404` / `409` (not `Delivered`) |

Errors are Problem Details; `DomainException` → 422, `DomainConflictException` → 409 (existing mapping). Frontend `types/api.ts` mirrors this table (ADR-18).

## Testing Strategy

| Layer | What | Approach |
|---|---|---|
| Domain unit | `CarouselSlide` ctor/`Update` invariants | `tests/Maxkeys.Domain.Tests/Carousel/CarouselSlideTests.cs` |
| Application (Testcontainers, ADR-09) | Each use case; `GetCarousel` hides inactive slide/product and resolves override vs product image; `ListBuyers` paging/search and 2-query shape; `RequestDeliveryResend` writes the event with `requestedBy` and 409s on non-Delivered; `OrderDeliveryResendHandler` sends via `RecordingEmailSender`; `OrderDeliveredHandler` unchanged behaviour | `PostgresFixture` per existing tests |
| Api (WebApplicationFactory) | 401/403/200 per new group using `Hs256ApiTestFixture.AdminSub`; `/admin/me`; public `/catalog/carousel` anonymous; resend 202/409 | `tests/Maxkeys.Api.Tests/Admin/*` |
| Frontend (Vitest) | `adminMiddleware.spec.ts` (redirect on 403, pass on 200), `HeroCarousel.spec.ts` (renders API slides, static fallback), `productImage.spec.ts` (placeholder) | Mock `useApi`, mirror `authMiddleware.spec.ts` |

## PR Slicing (stacked to `main`, per repo, budget 800)

| # | Repo | Scope | Est. authored lines | Verify |
|---|---|---|---|---|
| 1 | back | D3 + `/admin/me` + tests | ~400 | `dotnet test --filter Admin` |
| 2 | back | D2 domain/EF/migration + CRUD + `GET /catalog/carousel` + tests | ~600 (migration/snapshot generated, excluded; flag in PR body) | `dotnet test --filter Carousel` |
| 3 | back | D4 + D1 + fulfillment spec delta + tests | ~450 | `dotnet test --filter "Buyers\|Resend"` |
| 4 | front | `admin.ts` + catalog/carousel pages + `HeroCarousel` + D6 + tests | ~550 | `npm run test`; needs 1+2 deployed |
| 5 | front | buyers page + resend action + tests | ~250 | `npm run test`; needs 3 deployed |

## Threat Matrix

N/A — no routing, shell, subprocess, VCS/PR automation, executable-file classification, or process-integration boundary. Admin input boundaries are covered by `AdminPolicy` (fail-closed), domain invariants, and the Api tests above.

## Migration / Rollout

`AddCarouselSlides` runs via the existing `--migrate` release step (ADR-13); `Down` drops the table. Backend slices deploy before their frontend counterparts (ADR-18). After slice 1 ships, `--seed-catalog` must be treated as bootstrap-only: re-running it upserts by slug and would overwrite admin edits — update `docs/local-demo.md` and `docs/runbook-sandbox.md` in slice 1.

## Open Questions

- [ ] Should `--seed-catalog` skip existing slugs (insert-only) once admin editing exists? Proposed follow-up, not in scope.
- [ ] Resend has no rate limit; acceptable for 2 admins, revisit if abused.
