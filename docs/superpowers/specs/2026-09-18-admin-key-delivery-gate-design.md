# Admin key delivery gate — design

Date: 2026-09-18
Status: approved (chat), pending spec review

## Context

Vault fulfillment (2026-09-17 design) already auto-assigns keys atomically on
payment approval (`OrderApprovedHandler.AutoAssignVaultKeysAsync`, all-or-
nothing per item, protected by `Key.xmin`). `Order.AttachKey` transitions the
order straight to `OrderStatus.Delivered` the moment every item is complete,
and that same transition enqueues an `OrderDelivered` outbox event that
`OrderDeliveredHandler` turns into an immediate plaintext-key email to the
buyer. `GetMyOrder` mirrors this: it decrypts and returns every assigned key
as soon as `Status == Delivered`, with no separate reveal step.

`ListBuyers`/`AdminBuyersEndpoints` (admin-buyers spec, design D4) already
list every paid buyer with nested orders/items and an `AssignedKeys` count,
but expose no action — an admin can look, not act.

Goal: keys still auto-assign atomically and silently on payment approval (no
behavior change there), but the buyer must never see a key until an admin
explicitly reviews and delivers the order from `/admin/buyers`. Automatic
email delivery is turned off for now. The buyer only ever sees a key by
clicking "revelar key" per purchased product in `/account/orders`, which
records a permanent `Revealed` state.

**Scope**: backend only. Frontend (`/admin/buyers` actions, `/account/orders`
reveal button) is a separate repo/session — this design's job is the REST
contract; the UI work happens there once these endpoints exist.

## Decisions (already confirmed with user)

1. **New intermediate order status `KeysAssigned`**, not a side-channel
   boolean. `Order.AttachKey` now stops at `KeysAssigned` when every item is
   complete; only an explicit admin action (`Order.MarkDelivered`) advances
   `KeysAssigned -> Delivered`. `Delivered` again means what its name says:
   released to the buyer.
2. **Reveal granularity: per order item**, not per individual key. One
   button per purchased product; a quantity-3 item reveals all 3 keys in one
   click.
3. **Two separate admin actions**, not one combined button: "Asignar"
   (manually (re)run the same all-or-nothing auto-assign pass on demand, for
   orders where stock wasn't available at approval time) and "Entregar"
   (`MarkDelivered`, only valid once every item is complete).
4. **No automatic email on deliver.** `MarkDelivered` only flips buyer
   visibility in `/account/orders`; it does not enqueue `OrderDelivered`.
   `RequestDeliveryResend`/`OrderDeliveryResendHandler` (admin-triggered
   manual email) are untouched and remain available as an opt-in action once
   an order is `Delivered`. No feature flag for a future full-auto mode —
   YAGNI; revisit as its own change when asked.

## Domain changes

- `OrderStatus`: insert `KeysAssigned` between `AwaitingFulfillment` and
  `Delivered`. Stored as text (`HasConversion<string>()`), so inserting mid-
  enum is safe — no ordinal shift for existing rows.
- `Order.AttachKey`: the completion branch sets `Status = KeysAssigned`
  instead of `Delivered`/`DeliveredAt`. Still requires
  `Status == AwaitingFulfillment` to start, unchanged otherwise. Return value
  keeps its current meaning ("did this call complete every item") — callers
  that used it to gate an `OrderDelivered` outbox insert stop doing that.
- `Order.MarkDelivered(DateTimeOffset now)` (new): valid only from
  `KeysAssigned`; else `DomainConflictException`. Sets `Status = Delivered`,
  `DeliveredAt = now`. No outbox side effect.
- `Key`: add `KeyStatus.Revealed` and `DateTimeOffset? RevealedAt`. Add
  `Reveal(DateTimeOffset now)`: valid only from `Assigned`, else
  `DomainConflictException`; sets `Status = Revealed`, `RevealedAt = now`.

## Application layer

### Extract `AssignVaultKeysToOrder` (`src/Maxkeys.Application/Fulfillment/`)

Lifts `OrderApprovedHandler.AutoAssignVaultKeysAsync`'s body (per-item
all-or-nothing stock query + `order.AttachKey` loop) into its own class so it
can be called from two places without duplicating the stock query:

- `OrderApprovedHandler` (automatic, on payment approval — unchanged
  trigger/timing, just delegates now)
- the new admin "Asignar" endpoint (manual, on demand)

Returns a result the admin UI can render directly, e.g. per item: quantity,
already-assigned count, still-missing count; plus whether every item is now
complete. Same concurrency handling as today (`xmin` on `Key`, caller
retries once on `DbUpdateConcurrencyException` — this moves from
`OrderApprovedHandler` into the new admin use case too, since it now shares
the same read-then-write race).

### `OrderApprovedHandler`

Delegates the assign loop to `AssignVaultKeysToOrder`. The "awaiting
fulfillment" operator email now always sends after the assign attempt
(previously skipped when the order reached `Delivered` on its own — that
can no longer happen from this handler, since completion now stops at
`KeysAssigned` and still needs an admin to review/deliver either way).

### New: `DeliverOrder`

- Loads the order (+ items/keys), 404 (`null`) if unknown.
- Calls `order.MarkDelivered(now)` — surfaces `DomainConflictException` as
  409 if not `KeysAssigned` (e.g. admin double-clicks, or an item still
  lacks stock).
- `SaveChangesAsync`, then `ILogger.LogInformation` with order id + admin
  sub — key-lifecycle audit trail (requirement: log the full key lifecycle).

### New: `RevealOrderItemKeys`

- Same ownership pattern as `GetMyOrder`: `null` if the order doesn't exist
  or isn't owned by the caller (mapped to 404, never distinguishing the two
  — ADR-14).
- `DomainConflictException` (409) if `Order.Status != Delivered`.
- `DomainException` if the item doesn't belong to the order.
- For every `Assigned` key under that item: `key.Reveal(now)`, decrypt in
  memory (`KeyCipher`, same as `DeliveryEmailItems`).
- `SaveChangesAsync`, then `ILogger.LogInformation` with user id + order id +
  item id + revealed count.
- Returns the decrypted codes.

### `GetMyOrder` / `MyOrderItemDetail`

Stops auto-decrypting on `Status == Delivered`. New shape per item:

- `Keys`: decrypt only keys already `Revealed` (so a buyer who reveals once
  keeps seeing the code on later visits — no re-triggering the domain call).
- `Revealable` (bool, new field): `true` when `Order.Status == Delivered`
  and the item has at least one `Assigned` (not yet revealed) key. Frontend
  uses this to show/hide the "revelar key" button.

### `ListBuyers` / `AdminBuyerOrderItem`

Add `RevealedKeys` (int) alongside the existing `AssignedKeys` count, so the
admin table shows the full lifecycle (`Quantity` / `AssignedKeys` /
`RevealedKeys`) per purchased product without a second call.

### Remove `OrderDeliveredHandler`

Nothing enqueues `OutboxEventTypes.OrderDelivered` anymore (removed from
both `Order.AttachKey` call sites — `OrderApprovedHandler`'s path via
`AssignVaultKeysToOrder`, and `AttachKeyToOrderItem`). The handler, its DI
registration, and `OrderDeliveredHandlerTests` are dead code — delete them.
`OutboxEventTypes.OrderDelivered` constant is removed too (nothing produces
or consumes it). `DeliveryEmailItems` stays — `OrderDeliveryResendHandler`
still uses it for the manual resend path.

## API

`src/Maxkeys.Api/Endpoints/AdminEndpoints.cs` (existing `/admin/orders`
group, same `AdminPolicy` convention):

- `POST /admin/orders/{id}/assign-keys` → `AssignVaultKeysToOrder`. 404 if
  order unknown. Returns per-item assignment outcome.
- `POST /admin/orders/{id}/deliver` → `DeliverOrder`. 404 if order unknown,
  409 if not `KeysAssigned`. Returns updated order status.

`src/Maxkeys.Api/Endpoints/MeEndpoints.cs` (existing `/me/orders` group,
`RequireAuthorization()`):

- `POST /me/orders/{id}/items/{itemId}/keys/reveal` → `RevealOrderItemKeys`.
  404 if order unknown/not owned, 409 if order isn't `Delivered`, 422
  (`DomainException`, global mapping — `ProblemDetailsExceptionHandler`) if
  the item id doesn't belong to that order. Returns the decrypted codes for
  that item.

## Migration

One EF Core migration: `Key.RevealedAt` (nullable `timestamptz`). Both
`OrderStatus` and `KeyStatus` are stored as text (`HasConversion<string>()`),
so the new enum members need no data migration.

## Testing

- Domain: `Order.MarkDelivered` (happy path, conflict from every non-
  `KeysAssigned` status), `Order.AttachKey` now asserts `KeysAssigned` not
  `Delivered` on completion, `Key.Reveal` (happy path, conflict from
  non-`Assigned`).
- Application: `AssignVaultKeysToOrder` (moved/adapted from the existing
  `OrderApprovedHandlerTests` auto-assign cases), `DeliverOrder` (happy path,
  409 when incomplete, 404 unknown), `RevealOrderItemKeys` (happy path,
  ownership 404, 409 when not yet `Delivered`, wrong item id → `DomainException`).
  `GetMyOrder` tests rewritten: keys hidden until revealed even after
  `Delivered`; `Revealable` flag true/false cases.
- API: new endpoint tests mirroring `AdminEndpoints`/`MeEndpoints` existing
  patterns (auth-required, 404/409, happy path).
- Delete `OrderDeliveredHandlerTests`.
- New EF Core migration for `Key.RevealedAt`.

## Out of scope (YAGNI)

- Feature flag to re-enable full auto-delivery-with-email later — separate
  change when requested.
- Per-key (vs per-item) reveal — rejected, per decision 2.
- Any frontend work — separate repo/session.
