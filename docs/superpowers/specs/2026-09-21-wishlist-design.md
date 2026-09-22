# Wishlist — design

Date: 2026-09-21
Status: approved (chat), pending spec review

## Context

Logged-in buyers currently have no way to save a product for later. Product
cards on the catalog and the product detail page show a price sourced from
`VariantPricing.PickDisplayVariant` (the `IsRecommended` active variant,
falling back to the cheapest active one) — the same rule the wishlist listing
must reuse so prices always match what the buyer sees elsewhere.

Goal: a wishlist scoped to one product (not a specific variant), so it
survives price/variant changes and needs no variant-selection UI. Only
logged-in users can use it — guests get nothing (no localStorage fallback,
no guest-then-claim merge like orders have).

**Scope**: backend only. Frontend (star icon, hover behavior, catalog card
and product-detail placement) is a separate repo/session
(`chekeys-fronend`) — this design's job is the REST contract; the UI work
happens there once these endpoints exist.

## Decisions (already confirmed with user)

1. **References `Product`, not `ProductVariant`.** Adding from the catalog
   or from the product detail page behaves identically — there is no variant
   selection step for a wishlist add. Display price is always resolved at
   read time from the product's current display variant.
2. **Remove endpoint keys by `productId`**, not by the wishlist row's own
   id — the client already has `productId` from the catalog/detail response,
   no need to hand it a second identifier to track.
3. **Deactivated products are filtered out** of `GET /me/wishlist`, same
   rule the public catalog already applies (`Products.Where(p => p.IsActive)`)
   — no "unavailable" placeholder state.

## Domain changes

New entity `WishlistItem` (`src/Maxkeys.Domain/Wishlist/WishlistItem.cs`):

- `UserId` (Guid), `ProductId` (Guid), `CreatedAt` (DateTimeOffset).
- No mutation methods — a wishlist row is either present or gone.
- Constructor validates `userId`/`productId` are non-empty (`DomainException`
  otherwise), same defensive style as other entities in this codebase.

## Application layer

New folder `src/Maxkeys.Application/Wishlist/`, one class per use case
(ADR-02):

### `AddToWishlist`

- `ExecuteAsync(Guid userId, Guid productId, CancellationToken)`.
- Loads the product; returns `null` if unknown or `IsActive == false` (API
  maps to 404 — mirrors `GetProductBySlug`'s not-found handling).
- Idempotent: if a row for `(userId, productId)` already exists, no-op and
  return success. Insert-then-catch on the unique index (same
  `DbUpdateException` retry-by-reload shape as `ProcessPaymentNotification`'s
  `RecordAsync`, simplified since there's no state machine here — on
  conflict just re-check existence and return success either way).

### `RemoveFromWishlist`

- `ExecuteAsync(Guid userId, Guid productId, CancellationToken)`.
- Deletes the row if present; no-op (still success) if not — idempotent
  delete, matches `DeleteProductVariant`'s spirit but never 404s, since
  "already not in your wishlist" is not an error for the caller.

### `GetMyWishlist`

- `ExecuteAsync(Guid userId, CancellationToken)`.
- Joins `WishlistItems` (by `userId`) → `Products` (`IsActive` only,
  inactive/deleted products silently drop out per decision 3) →
  `ProductVariants` (active), picks the display variant via the existing
  `VariantPricing.PickDisplayVariant`.
- Returns `IReadOnlyList<WishlistItemSummary>`, same shape as
  `ProductSummary` plus the wishlist's own `AddedAt`:
  `(Guid ProductId, string Slug, string Name, string Platform, string?
  ImageUrl, decimal Price, decimal? OldPrice, DateTimeOffset AddedAt)`.
- Ordered by `AddedAt` descending (most recently added first).

## API

New `src/Maxkeys.Api/Endpoints/WishlistEndpoints.cs`, mapped in
`Program.cs` alongside the other endpoint groups:

```
var group = app.MapGroup("/me/wishlist").RequireAuthorization();
```

Same `ClaimsPrincipal user` + `RequireUserId(user)` pattern as
`MeEndpoints` (the private helper gets duplicated, not shared across
files — matches the existing `TryGetUserId`/`RequireUserId` split, each
endpoint file owns its own claim-extraction helper).

- `GET /me/wishlist` → `GetMyWishlist`. 200, list (possibly empty).
- `POST /me/wishlist` body `{ ProductId }` → `AddToWishlist`. 204 on
  success, 404 if the product doesn't exist or is inactive.
- `DELETE /me/wishlist/{productId:guid}` → `RemoveFromWishlist`. 204
  always (idempotent, no 404).

## Migration

One EF Core migration: new `wishlist_items` table —
`id` (pk), `user_id`, `product_id`, `created_at`. Unique index on
`(user_id, product_id)`. FK `product_id -> products.id`, no cascade
behavior needed since `Product` is never hard-deleted (only
`DeleteProduct`'s soft delete via `IsActive`).

## Testing

- Domain: `WishlistItem` constructor validation (empty `userId`/`productId`
  throws).
- Application: `AddToWishlist` (happy path, 404 for unknown/inactive
  product, double-add is a no-op not a conflict), `RemoveFromWishlist`
  (happy path, removing a non-existent row is still success),
  `GetMyWishlist` (happy path with recommended-variant pricing, falls back
  to cheapest active variant when none is recommended, excludes inactive
  products, empty list for a user with nothing saved).
- API: new `WishlistEndpointsTests` mirroring `MeEndpoints`'s existing
  pattern (auth required on all three routes, 404/204 cases, happy path).
- New EF Core migration for `wishlist_items`.

## Out of scope (YAGNI)

- Guest wishlist / localStorage-then-claim-on-login merge (unlike orders,
  no such requirement was raised) — revisit only if asked.
- Wishlist item notes, price-drop alerts, or any notification tie-in.
- Any frontend work — separate repo/session (`chekeys-fronend`).
