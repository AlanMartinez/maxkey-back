# Exploration: Admin dashboard (2 admins, whitelist auth; carousel + catalog + buyer management)

> Source of truth: Engram `sdd/admin-dashboard/explore` (observation #294). Mirrored here for hybrid artifact-store mode.

## Current State

**Admin auth already exists and is reusable.** `src/Maxkeys.Api/Auth/AdminPolicy.cs` defines an `Admin` authorization policy backed by `AdminAuthorizationHandler`, which succeeds only when the caller's Supabase JWT `sub` claim is present in `AuthOptions.AdminSubs` (a configured string allowlist). Empty allowlist fails closed. It is spec'd (`openspec/changes/mvp-marketplace/specs/auth/spec.md`, "Admin Authorization Policy") and used today by `AdminEndpoints.cs` (`/admin/orders`, `/admin/orders/{id}/items/{itemId}/keys`). No new auth mechanism is needed: add the 2 admin `sub` values to `Auth:AdminSubs` and put `.RequireAuthorization(AdminPolicy.Name)` on new admin routes.

**Catalog domain** (`src/Maxkeys.Domain/Catalog/Product.cs`, `ProductVariant.cs`) already has `Slug/Name/Platform/IsActive/ImageKey/Description` on `Product` and mutation methods `Product.UpdateCatalogInfo(...)` / `ProductVariant.UpdateDetails(...)` (added for `CatalogSeeder`, ADR-12). Directly reusable by admin edit/toggle use cases.

**ADR-12 tension** (`openspec/changes/mvp-marketplace/design.md`, line ~573): "Catalog managed by seed file; R2 as public URL builder only (user-approved deviation, 2026-09-12). No S3 SDK, no S3 credentials, no upload endpoint in MVP." Images are uploaded manually via the Cloudflare R2 dashboard; the API only composes `{Storage:R2PublicBaseUrl}/{ImageKey}` via `ImageUrlBuilder` (`src/Maxkeys.Application/Catalog/StorageOptions.cs`). The risk register anticipated "upgrade to endpoints when an admin UI arrives", but real image upload reverses a user-approved decision and needs fresh sign-off.

**Carousel has zero backend representation.** `maxkeys-front/components/catalog/HeroCarousel.vue` hard-codes a `slides` array (local `/images/promos/*.png`). No API call, table, or entity. Admin-editable carousel requires a new domain concept (e.g. `CarouselSlide`).

**No Users table.** `AppDbContext` DbSets: `Products, ProductVariants, Orders, OrderItems, Keys, OutboxEvents, ProcessedWebhookNotifications`. `Order.UserId` is a nullable `Guid` referencing Supabase `auth.users` (not mirrored); buyers are otherwise identified by `Order.BuyerEmail`. "Manage users who bought keys" has no entity to manage; it would be a view over `Orders`/`OrderItems`/`Keys`. No revoke/reissue/ban concept exists.

**No admin UI framework in the API.** `Program.cs` is pure minimal APIs. The only HTML route, `/dev/payments/{orderId}` (`DevPaymentEndpoints.cs`), is a raw string-templated Development-only demo page, not a UI foundation.

**Frontend (`maxkeys-front`, Nuxt 3 / Vue 3 / TS)** already has Supabase auth wiring (`useSupabaseUser`/`useSupabaseSession`), a route-guard pattern (`middleware/auth.ts`), and a bearer-attaching `useApi()` composable. `utils/productImage.ts` hardcodes a local image override map (`catalogImages`) for the 4 seeded products that takes priority over `product.imageUrl`; it would hide any admin image change unless removed.

Architecture: Clean/Hexagonal (`Maxkeys.Domain` / `Maxkeys.Application` with `IAppDbContext` port and one class per use case, ADR-02 / `Maxkeys.Infrastructure` EF Core + Postgres + migrations + `CatalogSeeder` / `Maxkeys.Api` minimal APIs + auth). Two repos/deploys (ADR-18): `maxkeys-back` (owns `openspec/`) and `maxkeys-front` (Nuxt, Vercel). Tests: Testcontainers Postgres for Application/Api tests (ADR-09), per-project test folders mirroring `src/`.

## Affected Areas

- `src/Maxkeys.Api/Auth/AdminPolicy.cs`, `AuthOptions.cs` — reuse as-is; extend `Auth:AdminSubs`
- `src/Maxkeys.Api/Endpoints/AdminEndpoints.cs` (or new siblings `AdminCatalogEndpoints.cs`, `AdminOrdersEndpoints.cs`) — new admin routes with `.RequireAuthorization(AdminPolicy.Name)`
- `src/Maxkeys.Domain/Catalog/Product.cs`, `ProductVariant.cs` — reuse `UpdateCatalogInfo`/`UpdateDetails`; possibly an `IsActive`-only toggle
- `src/Maxkeys.Application/Catalog/*` — new admin use cases (list including inactive, update, toggle), ADR-02 style
- `src/Maxkeys.Application/Persistence/IAppDbContext.cs`, `src/Maxkeys.Infrastructure/Persistence/AppDbContext.cs`, `Configurations/` — new `CarouselSlide` entity + EF config + migration
- `src/Maxkeys.Application/Catalog/StorageOptions.cs` (`ImageUrlBuilder`) — reused for "paste ImageKey"; R2 SDK only if real upload is approved
- `openspec/changes/mvp-marketplace/design.md` — ADR-12 delta note if upload scope is added
- `maxkeys-front/components/catalog/HeroCarousel.vue` — refactor to fetch slides from API
- `maxkeys-front/utils/productImage.ts` — remove hardcoded override map
- `maxkeys-front/middleware/auth.ts`, `composables/useApi.ts` — pattern to copy for `middleware/admin.ts` + `pages/admin/*.vue`
- `tests/Maxkeys.Api.Tests/`, `tests/Maxkeys.Application.Tests/`, `tests/Maxkeys.Domain.Tests/` — no admin-catalog/carousel/buyer tests exist yet

## Approaches

### (a) Admin authentication / authorization

1. **Reuse existing Supabase JWT + `AdminSubs` allowlist** — Pros: zero new auth code; per-admin identity preserved for audit (`AttachKeyToOrderItem` already records `adminSub`); no new secret. Cons: adding/removing an admin needs config change + redeploy (fine for 2). Effort: Low. **Recommended.**
2. **New static secret-key header** (`X-Admin-Key`) — Pros: no Supabase account needed. Cons: duplicates existing infra, loses per-admin audit identity, one more secret to rotate. Effort: Low, net-negative.
3. **New admin-users table with credentials/roles** — Pros: room to grow. Cons: overkill for 2 admins, duplicates Supabase Auth. Effort: Medium-High.

### (b) Where the admin UI lives

1. **Protected routes in `maxkeys-front`** (`pages/admin/*.vue`, `middleware/admin.ts`) — reuses auth wiring, `useApi()`, design system; consistent with ADR-18. Cons: shares the Nuxt bundle (mitigated by route splitting). Effort: Low-Medium. **Recommended.**
2. **Server-rendered pages in `Maxkeys.Api`** — `/dev/payments` is a throwaway dev page; would introduce a new UI paradigm into a JSON API and break ADR-18. Effort: Medium-High. Not recommended.
3. **Separate admin app/repo** — third deploy pipeline; disproportionate. Effort: High. Not recommended.

### (c) Image storage / admin image editing

1. **`ImageKey` stays a pasted string** (upload manually via Cloudflare dashboard, as today) — zero ADR-12 deviation; admin PATCH exposes `ImageKey`/`Description`/`IsActive`. Cons: not true self-service. Effort: Low. **Recommended for v1.**
2. **Real upload endpoint** (S3-compatible SDK to R2, or presigned PUT) — matches literal request. Cons: reverses ADR-12's user-approved line; new credentials, validation, threat-matrix entry. Effort: Medium.
3. **Local disk / `wwwroot`** — does not fit two-repo / ephemeral container model. Not recommended.

### Carousel sub-decision

1. New `CarouselSlide` entity/table (Title, Alt, ImageKey, LinkUrl/Cta, SortOrder, IsActive) + admin CRUD + public read endpoint (`GET /catalog/carousel`) + `HeroCarousel.vue` refactor. Effort: Medium. **Recommended** (only way to satisfy the request).
2. Leave carousel hardcoded — does not satisfy "change carousel images". Not recommended.

## Recommendation

Reuse `AdminPolicy`/`AdminSubs` (a.1). Build the admin UI as a protected area in `maxkeys-front` (b.1). Ship v1 image editing as "paste an existing `ImageKey`" (c.1), treating real upload (c.2) as a separate explicitly-approved follow-up. Add a small `CarouselSlide` domain slice. Scope "manage buyers" as a read/audit view over `Orders`/`OrderItems`/`Keys` grouped by buyer email unless the user explicitly wants mutation actions (resend email, revoke/reissue key) that do not exist in the domain today.

## Risks

- ADR-12 conflict: "change images" may imply upload, which ADR-12 forbids as a user-approved deviation; needs fresh approval or default to paste-ImageKey.
- Carousel is entirely new domain surface; easy to underestimate.
- "Manage users who bought keys" has no Users entity; scope must be precise (view vs. mutate).
- `productImage.ts` override map will silently hide admin image changes unless removed.
- Spans 2 repos and 3 sub-features; likely exceeds the review budget; plan chained/sliced PRs.

## Open Questions (for proposal)

1. Real image upload from the dashboard vs. paste R2 `ImageKey` for v1?
2. Carousel slides: freeform (title/image/link/cta) or linked to a product? New `CarouselSlide` entity/table acceptable?
3. What does "administrar los usuarios que ya compraron key" concretely mean: read-only buyer/order list, resend delivery email, revoke/reissue key?
4. Do the 2 admin Supabase accounts exist? Is config-only `Auth:AdminSubs` management acceptable?
5. Confirm reusing Supabase JWT + `AdminSubs` as the sole admin auth mechanism.

## Ready for Proposal

Yes, pending answers to the open questions above.
