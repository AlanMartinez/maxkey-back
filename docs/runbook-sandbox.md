# Sandbox end-to-end runbook

This runbook proves the full MVP purchase flow against real external services (Mercado
Pago sandbox, Supabase, SMTP) instead of test doubles. Run it once before the first
production deploy, and record the result in the [pass/fail table](#pass-fail-record)
at the bottom (task 18a.5). It exercises the proposal's Success Criteria: browse → pick
a variant → 2-item cart → guest/authenticated checkout → sandbox payment approval →
webhook → operator fulfillment → delivery email → order visible in "Mis compras".

## Quick path

1. Set the [prerequisites](#prerequisites) below (env vars, tunnel, seed data, test user).
2. Run through [steps 1–9](#step-by-step) with the provided `curl` calls.
3. Confirm each [verification query](#verification-queries) matches its expected row.
4. Fill in the [pass/fail table](#pass-fail-record) and commit it back to this file.

## Prerequisites

| # | Item | How |
|---|------|-----|
| 1 | Backend running with a real Postgres | Locally: `dotnet run --project src/Maxkeys.Api` against a Postgres instance (local or a Supabase dev project), with `ConnectionStrings__Default` set. Migrated first: `dotnet run --project src/Maxkeys.Api -- --migrate`. |
| 2 | Catalog seeded | `dotnet run --project src/Maxkeys.Api -- --seed-catalog seed/catalog.json` (run from the repo root — see the note on `--seed-catalog`'s working-directory requirement in `Program.cs`'s XML doc). Bootstrap-only once admin catalog editing exists: it upserts by slug and would overwrite any edits made through `/admin/catalog/*`. |
| 3 | Supabase JWT signing mode confirmed (task 10.0 — **resolved: JWKS/ES256**) | Supabase dashboard → Project Settings → API → JWT Settings. Older projects sign with a shared **HS256** secret ("JWT Secret" field); newer projects use **JWKS** (asymmetric, no shared secret exposed). Set `Auth__Mode` to match (`Jwks` default, or `Hs256` + `Auth__Hs256Secret`). Confirmed for this project: the public JWKS publishes an EC/ES256 key, so keep `Auth__Mode=Jwks`. |
| 4 | Supabase test user | Create one Supabase Auth user (email/password or Google) and obtain its access token (Supabase client `session.access_token`, or the `/auth/v1/token?grant_type=password` REST call). This is the buyer's bearer token in step 4 and the token used in step 9. |
| 5 | `Auth__AdminSubs` includes the operator | Add the operator Supabase user's `sub` (its Supabase user id) to `Auth__AdminSubs`. This is the bearer token used in step 7 (key attachment). |
| 6 | Mercado Pago sandbox credentials | MP Developer dashboard → sandbox application → copy the sandbox `Access Token` into `Payments__AccessToken`. Create a sandbox **test buyer** (Sellers/Buyers panel) for step 5's checkout approval. |
| 7 | MP webhook secret + signature manifest (**open item — task 11.6**) | MP dashboard → your application → Webhooks → copy the **signature secret** into `Payments__WebhookSecret`. `MercadoPagoSignatureValidator` rebuilds the manifest `id:<data.id>;request-id:<x-request-id>;ts:<ts>;` from the notification's `x-signature`/`x-request-id` headers and `data.id` query param (design section 6b) — this has only been unit-tested with synthetic vectors; running a real sandbox notification through it end-to-end is this runbook's step 6, and is what closes task 11.6. |
| 8 | Public tunnel for the webhook | MP cannot reach `localhost`. Start one (e.g. `ngrok http 8080`) and set `Payments__NotificationUrl` and the MP application's webhook URL to `https://<tunnel>/webhooks/mercadopago`. |
| 9 | Email sender | Local run: leave `Email__Sender=Logging` and read the delivery email from console/Serilog output instead of an inbox. To test real SMTP delivery, set `Email__Sender=Smtp` and fill `Email__Smtp__*`/`Email__From`/`Email__OperatorTo`. |
| 10 | `psql` access | For the [verification queries](#verification-queries), against the same database the API uses. |

## Step-by-step

All requests assume the API is reachable at `http://localhost:8080`. Replace
`<BUYER_JWT>` and `<ADMIN_JWT>` with the Supabase access tokens from prerequisites 4/5.

### 1. Browse the catalog

```bash
curl http://localhost:8080/catalog/products
```

Expected: `200`, a JSON array of 4 seeded products (`steam-wallet-gift-card`,
`playstation-plus-membership`, `fc-25-points`, `xbox-game-pass-ultimate`), each with
`fromPrice`/`imageUrl` and no stock counter.

### 2. Pick a variant

```bash
curl http://localhost:8080/catalog/products/steam-wallet-gift-card
```

Expected: `200`, `variants[]` with a `Guid` `id` per variant. Record two variant ids
from two different products (they become `<VARIANT_A>` and `<VARIANT_B>` below) —
the Success Criteria's "2 items, one with qty 2" cart.

### 3. Build the cart

Client-side only (no API call): `<VARIANT_A>` × 1, `<VARIANT_B>` × 2 — 3 total units,
matching the "operator attaches 3 keys" criterion.

### 4. Checkout

```bash
curl -i -X POST http://localhost:8080/checkout/orders \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <BUYER_JWT>" \
  -d '{
    "email": "buyer@example.com",
    "items": [
      { "variantId": "<VARIANT_A>", "quantity": 1 },
      { "variantId": "<VARIANT_B>", "quantity": 2 }
    ]
  }'
```

Expected: `201`, body `{ "orderId": "<ORDER_ID>", "initPoint": "https://sandbox.mercadopago.com/..." }`.
Record `<ORDER_ID>`.

> The bearer token is optional (`OptionalBearerFilter` — a request with no
> `Authorization` header still succeeds as a guest order). It is included here
> because the last step of this runbook ("Mis compras") requires the order to be
> linked to a buyer's `user_id`; a purely anonymous guest order has no `user_id`
> and will not appear under `/me/orders`, only in the status-polling response
> (step 5) and an admin listing. Omit the header to test the guest path instead;
> substitute polling `GET /checkout/orders/{id}/status` for step 9.

### 5. Approve the payment in the MP sandbox

Open `initPoint` in a browser. Log in with the **sandbox test buyer** (prerequisite
6) and approve the payment. MP redirects to the configured `back_url`
(`Frontend:BaseUrl` + `/checkout/result?...`); the query string is not required for
this backend-only runbook.

Poll the order status until it flips (should happen within seconds of approval,
once the webhook in step 6 lands):

```bash
curl http://localhost:8080/checkout/orders/<ORDER_ID>/status
```

Expected immediately after approval, before the webhook arrives: `status: "Pending"`.
After the webhook (step 6) is processed: `status: "Paid"`, then `"AwaitingFulfillment"`
once the outbox processor runs (poll interval `Outbox__PollIntervalSeconds`, default 5s).

### 6. Confirm the webhook was processed exactly once

MP sends the notification to the tunnel URL automatically after approval — no manual
call needed. Watch the API's Serilog output for one `POST /webhooks/mercadopago` →
`200`. If MP retries the same notification (it does, by design, until it gets `200`),
[verification query 1](#verification-queries) must still show exactly one row.

### 7. Operator attaches 3 keys

```bash
curl -X POST http://localhost:8080/admin/orders/<ORDER_ID>/items/<ITEM_ID_A>/keys \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <ADMIN_JWT>" \
  -d '{ "code": "SANDBOX-KEY-A1" }'

curl -X POST http://localhost:8080/admin/orders/<ORDER_ID>/items/<ITEM_ID_B>/keys \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <ADMIN_JWT>" \
  -d '{ "code": "SANDBOX-KEY-B1" }'

curl -X POST http://localhost:8080/admin/orders/<ORDER_ID>/items/<ITEM_ID_B>/keys \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <ADMIN_JWT>" \
  -d '{ "code": "SANDBOX-KEY-B2" }'
```

Get `<ITEM_ID_A>`/`<ITEM_ID_B>` from `GET /admin/orders` (lists orders
`AwaitingFulfillment`) or from `GET /me/orders/<ORDER_ID>` (buyer view, no keys yet).

Expected: after the first two calls, `orderStatus: "AwaitingFulfillment"` with
per-item `assigned` counts (1/1 for item A, 1/2 for item B) — buyer still sees no
keys. After the third call (item B reaches `assigned: 2 == quantity: 2`),
`orderStatus: "Delivered"`.

### 8. Confirm one delivery email with all 3 keys

`Email__Sender=Logging` (default in dev): find the `LoggingEmailSender` log line for
`order.BuyerEmail` in the API console output — its body must list all 3 codes
(`SANDBOX-KEY-A1`, `SANDBOX-KEY-B1`, `SANDBOX-KEY-B2`) grouped by item.
`Email__Sender=Smtp`: check the inbox at `buyer@example.com` for exactly one email.

### 9. Keys visible in "Mis compras"

```bash
curl http://localhost:8080/me/orders \
  -H "Authorization: Bearer <BUYER_JWT>"

curl http://localhost:8080/me/orders/<ORDER_ID> \
  -H "Authorization: Bearer <BUYER_JWT>"
```

Expected: the order listing includes `<ORDER_ID>` with `status: "Delivered"`. The
detail response's `items[].keys` is populated (non-`null`) with the exact codes from
step 7 — this only happens because `Status == Delivered` (orders-history spec "Order
Detail With Conditional Key Reveal"); the same call before step 7 completes returns
`keys: null` for every item.

## Verification queries

Run against the same Postgres database the API uses.

```sql
-- 1. Webhook processed exactly once for this order's payment
SELECT count(*) FROM processed_webhook_notifications WHERE order_id = '<ORDER_ID>';
-- expected: 1

-- 2. Exactly one Pending -> Paid transition, one OutboxEvent(OrderApproved)
SELECT status, paid_at, delivered_at FROM orders WHERE id = '<ORDER_ID>';
-- expected: status = 'Delivered', paid_at and delivered_at both set

-- 3. Outbox processed both events (OrderApproved, OrderDelivered) exactly once
SELECT type, status, attempts FROM outbox_events
WHERE payload->>'orderId' = '<ORDER_ID>';
-- expected: two rows, both status = 'Processed', attempts = 1

-- 4. 3 keys assigned, encrypted at rest (not plaintext)
SELECT k.status, k.key_version, length(k.encrypted_code) AS ciphertext_len
FROM keys k
JOIN order_items oi ON oi.id = k.order_item_id
WHERE oi.order_id = '<ORDER_ID>';
-- expected: 3 rows, status = 'Assigned'; encrypted_code is never equal to the
-- plaintext code entered in step 7 (confirm by eye — it is binary ciphertext)
```

## Pass/fail record

Fill in after running the steps above (task 18a.5). Do not mark `Pass` on an
assumption — every row needs the corresponding evidence (log line, HTTP response, or
query result) checked at the time of the run.

| # | Check | Result | Evidence | Date | Run by |
|---|-------|--------|----------|------|--------|
| 1 | Browse → variant → 2-item cart (qty 1 + qty 2) built | ☐ Pass ☐ Fail | | | |
| 2 | Checkout creates the order, returns a sandbox `initPoint` | ☐ Pass ☐ Fail | | | |
| 3 | Sandbox payment approved as the MP test buyer | ☐ Pass ☐ Fail | | | |
| 4 | Webhook processed exactly once (query 1) | ☐ Pass ☐ Fail | | | |
| 5 | Order reached `AwaitingFulfillment` | ☐ Pass ☐ Fail | | | |
| 6 | Operator attached all 3 keys; order reached `Delivered` | ☐ Pass ☐ Fail | | | |
| 7 | Exactly one delivery email, all 3 keys listed | ☐ Pass ☐ Fail | | | |
| 8 | Keys visible in `GET /me/orders/{id}` only after `Delivered` | ☐ Pass ☐ Fail | | | |
| 9 | Task 10.0 — Supabase JWT signing mode confirmed and `Auth:Mode` set accordingly | ☐ Pass ☐ Fail | | | |
| 10 | Task 11.6 — real MP sandbox `x-signature` validated (not just unit-test vectors) | ☐ Pass ☐ Fail | | | |

**Overall**: ☐ Pass ☐ Fail — this runbook's own execution is task 18a.5, a manual
step; it is not automated (no CI harness reaches a real MP sandbox).
