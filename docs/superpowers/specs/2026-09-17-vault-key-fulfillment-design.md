# Vault key fulfillment — design

Date: 2026-09-17
Status: approved (chat), pending spec review

## Context

`Key` already exists (`Domain.Keys.Key`): `ProductVariantId`, `EncryptedCode`,
`Status` (`Available`/`Assigned`), `AssignTo`. Today an admin attaches exactly
one key to one order item manually, after payment approval, via
`AttachKeyToOrderItem`. `OrderApprovedHandler` marks the order
`AwaitingFulfillment` and emails the operator to fulfill it by hand.

Goal: let admins bulk-preload key stock per product variant ("vault"), with a
per-product active/inactive toggle. When active, keys auto-assign on payment
approval instead of requiring the manual step.

**Scope**: backend only. This repo (`maxkeys`) has no frontend — the admin
panel is a separate Vercel-deployed repo the user is coordinating directly
with another session (`maxkeys-front-2f`). This design's job is to produce a
clean REST contract; the collapsible-UI/UX work happens elsewhere.

## Decisions (already confirmed with user)

1. **Toggle granularity: per product**, not per variant. One `VaultEnabled`
   bool on `Product` gates every variant of that product.
2. **Partial stock: all-or-nothing per item.** If an order item needs qty 3
   and the vault has 2 available, auto-assign zero for that item — it falls
   back fully to the manual flow. No partial delivery.
3. **Operator email**: if vault auto-assignment fully delivers the order
   (every item complete, `Order.Status -> Delivered`), skip the "awaiting
   fulfillment" operator email — nothing is pending. If any item remains
   unfulfilled after the auto-assign attempt, send the email as today.

## Domain changes

- `Product`: add `bool VaultEnabled { get; private set; }` (default `false`)
  and `SetVaultEnabled(bool enabled)`. Kept as its own method — vault is a
  separate admin section, not part of the catalog edit form's "re-send full
  record" convention.
- `Key` gets an `xmin` concurrency token (`KeyConfiguration.cs`, same pattern
  as `OrderConfiguration.cs`). Today only `Order` has one. Without it, two
  orders for the same variant approved concurrently (multi-instance Fly, or
  two outbox events processed close together) could both read the same
  `Available` key before either commits and double-assign it. New EF
  migration.

## Application layer

New use cases (`src/Maxkeys.Application/Fulfillment/` — same bounded context
as `AttachKeyToOrderItem`, not a new module):

- **`LoadVaultKeys(productVariantId, codes[], loadedBy)`** — bulk-creates
  `Available` `Key` rows. Same validation/encryption path as
  `AttachKeyToOrderItem` (`KeyCipher`, non-empty `loadedBy`, non-empty code
  per line). Rejects an empty list.
- **`ListVaultStock()`** — per product: `Id`, `Name`, `VaultEnabled`; per
  variant: `Id`, display fields, `AvailableCount`, `AssignedCount`. Powers the
  other repo's collapsible admin view (product → variants → counts).
- **`ToggleProductVault(productId, enabled)`** — sets `VaultEnabled`, 404 if
  product not found.

### `OrderApprovedHandler` change

After `order.MarkAwaitingFulfillment(now)` and before deciding whether to
email the operator:

```
foreach item in order.Items where item is not already complete:
    product = load item's Product
    if !product.VaultEnabled: continue

    remaining = item.Quantity - item.Keys.Count(Assigned)
    available = query Keys where ProductVariantId == item.ProductVariantId
                   && Status == Available
                order by CreatedAt
                take remaining

    if available.Count < remaining: continue   // all-or-nothing: skip item

    foreach key in available:
        order.AttachKey(item.Id, key, now)      // existing domain method, unchanged

if order.Status became Delivered:
    db.OutboxEvents.Add(OrderDelivered event)   // same row AttachKeyToOrderItem.AttachAsync inserts
    // no operator email

await db.SaveChangesAsync()

if order.Status != Delivered:
    send operator email (unchanged existing behavior)
```

Concurrency: wrap in the same reload-and-retry-once pattern
`AttachKeyToOrderItem.ExecuteAsync` already uses for
`DbUpdateConcurrencyException` — catch, clear tracker, reload the order,
retry the whole auto-assign pass once, then let a second conflict surface as
a dead-lettered outbox event (existing retry/backoff handles it). Now safe
because `Key` carries its own `xmin`.

Queries for available keys use the existing
`ix_keys_product_variant_id_status` index — no new index needed.

## API

New group, same conventions as `AdminCatalogEndpoints.cs` (admin policy, full
DTOs, no PATCH):

- `GET /admin/vault/products` → `ListVaultStock` result
- `POST /admin/vault/variants/{id}/keys` → body `{ codes: string[] }`, calls
  `LoadVaultKeys`, 404 if variant unknown
- `PUT /admin/vault/products/{id}/toggle` → body `{ enabled: bool }`, calls
  `ToggleProductVault`, 404 if product unknown

## Testing

- Domain: `Product.SetVaultEnabled` (`Product.cs` test file).
- Application: `LoadVaultKeys` (happy path, empty list rejected), `ListVaultStock`,
  `ToggleProductVault`, and `OrderApprovedHandlerTests` extended with: full
  auto-assign (order goes `Delivered`, no operator email, `OrderDelivered`
  event inserted), partial stock (item skipped, operator email still sent),
  vault disabled (unchanged current behavior), concurrency retry (simulate
  `DbUpdateConcurrencyException` on the first pass).
- API: endpoint tests mirroring `AdminCatalogEndpointsTests` patterns
  (auth-required, 404s, happy path).
- New EF Core migration for `Product.VaultEnabled` + `Key.xmin`.

## Out of scope (YAGNI)

- Per-variant toggle (rejected — per-product only, per decision 1).
- Deleting/unloading individual vault keys — no such need stated; keys stay
  `Available` until consumed, same lifecycle as today.
- Any frontend work — separate repo, separate session.
