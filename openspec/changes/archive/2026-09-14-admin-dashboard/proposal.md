# Proposal: Admin Dashboard (catalog, carousel, buyers)

## Intent

Catalog edits require a seed-file redeploy, the hero carousel is hardcoded in `HeroCarousel.vue`, and buyer support (find order, resend keys) needs DB access. Give the two admins a protected UI plus admin API, keeping ADR-12 (no upload/S3 SDK) and ADR-18 (two repos) intact.

Binding decisions (Engram #295): paste existing R2 `ImageKey`; buyers = read-only view + resend delivery email; slides reference a product; reuse `AdminPolicy`/`Auth:AdminSubs`; UI in `maxkeys-front`.

## Scope

### In Scope

| Area | Deliverable |
|------|-------------|
| Catalog (back) | Admin list incl. inactive; update `Description`/`ImageKey`; toggle `IsActive` on products and variants (reuse `UpdateCatalogInfo`/`UpdateDetails`) |
| Carousel (back) | `CarouselSlide` (`ProductId`, `SortOrder`, `IsActive`, optional `Title`/`Caption`/`ImageKey` overrides); EF config + migration; admin CRUD; public `GET /catalog/carousel` |
| Buyers (back) | Paged/searchable list grouped by `BuyerEmail` with orders, items, keys (masked), status; `POST /admin/orders/{id}/resend-delivery` for `Delivered` orders |
| Tests (back) | Testcontainers + `WebApplicationFactory` conventions |
| Frontend | `middleware/admin.ts`, `pages/admin/{catalog,carousel,buyers}`, `HeroCarousel.vue` fetches API, drop `catalogImages` override in `utils/productImage.ts` |

### Out of Scope

- Image upload / R2 SDK; admin self-service, roles, ban
- Key revoke/reissue, refunds, order edits; freeform slides

## Capabilities

> `openspec/specs/` does not exist yet; current specs live in `openspec/changes/mvp-marketplace/specs/`.

### New Capabilities
- `admin-catalog`: admin listing and mutation of products/variants
- `carousel`: slide lifecycle, admin CRUD, public read contract
- `admin-buyers`: buyer/order/key read view and resend action

### Modified Capabilities
- `fulfillment`: "One-Time Delivery Email" gains an explicit, audited admin resend

## Approach

- New `/admin/*` groups with `.RequireAuthorization(AdminPolicy.Name)`; one use case per operation (ADR-02).
- Resend enqueues an outbox event consumed by `OrderDeliveredHandler` (or a thin sibling sharing `EmailTemplates.BuyerOrderDelivered`); no second email path. Design fixes event type and `adminSub` audit.
- Slide image/link derive from the product unless overridden; public read hides inactive products.
- Admin pages reuse `useApi()`; API 403 stays authoritative.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `src/Maxkeys.Api/Endpoints/Admin*Endpoints.cs` | New | Catalog, carousel, buyers routes |
| `src/Maxkeys.Application/{Catalog,Carousel,Buyers}/` | New | Use cases, DTOs |
| `src/Maxkeys.Domain/Carousel/CarouselSlide.cs` | New | Entity, invariants |
| `src/Maxkeys.Infrastructure/Persistence/` | Modified | `DbSet`, config, migration |
| `.../specs/fulfillment/spec.md` | Modified | Resend delta |
| `maxkeys-front/{middleware,pages/admin,components/catalog,utils}` | New/Modified | Admin UI, carousel fetch |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Resend duplicates emails (at-least-once) | Low | Explicit action, logged with `adminSub`; keys unchanged |
| Inactive product behind active slide | Med | Public query filters it; admin UI warns |
| Override removal exposes missing R2 keys | Med | Seed carries `ImageKey`; placeholder fallback |
| Exceeds review budget | High | Slices below |

## PR Slicing (stacked to main, per repo)

| # | Repo | Slice | Est. |
|---|------|-------|------|
| 1 | back | Admin catalog + tests | ~350 |
| 2 | back | Carousel slice + CRUD + public GET + tests | ~600 (migration excluded) |
| 3 | back | Buyers + resend + fulfillment delta + tests | ~450 |
| 4 | front | Guard + catalog/carousel pages + `HeroCarousel` + override removal | ~550 |
| 5 | front | Buyers page + resend | ~250 |

Frontend slices wait for the matching backend slice to deploy.

## Rollback Plan

- Slices revert independently; carousel migration `Down` drops the table.
- `HeroCarousel.vue` keeps a static fallback on empty/error.
- Admin routes are additive; clearing `Auth:AdminSubs` fails closed.

## Dependencies

- Two admin `sub`s in `Auth:AdminSubs`; images pre-uploaded to R2.

## Success Criteria

- [ ] Admin lists/edits/toggles products and variants; non-admin gets 403
- [ ] Storefront carousel renders admin-created slides in `SortOrder`
- [ ] Admin finds a buyer by email and resends delivery for a `Delivered` order
- [ ] Admin-edited `ImageKey` shows in storefront without code changes
- [ ] All slices pass `dotnet test` / `npm run test` within budget
