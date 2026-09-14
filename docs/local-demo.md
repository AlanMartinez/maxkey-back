# Local demo mode

Run the whole marketplace flow on your machine — browse, cart, checkout, "payment",
fulfillment, delivery email, order history — with a Docker Postgres, seeded catalog,
placeholder images and **no Mercado Pago, SMTP or Cloudflare R2 credentials**. Use it to
look at the UI and the flows; use [`runbook-sandbox.md`](runbook-sandbox.md) when you need
to prove the integration against the real external services.

## What is real and what is faked

| Piece | In local demo mode |
|-------|--------------------|
| Postgres, EF Core migrations, catalog seed | **Real** (`docker-compose.yml`, `--migrate`, `--seed-catalog`). |
| Order state machine, outbox, use cases | **Real** — the fake payment goes through `ProcessPaymentNotification`, the same use case the Mercado Pago webhook calls. |
| Supabase authentication (login, `Mis compras`, admin key attachment) | **Real** — you still need a Supabase project and a user; guest checkout works without it. |
| Mercado Pago | **Faked** — `FakePaymentGateway` (`Payments:Mode=Fake`) sends the buyer to an in-process page at `/dev/payments/{orderId}` with an "Approve payment" button. |
| Delivery / operator emails | **Faked** — `Email:Sender=Logging` prints them to the API console instead of sending. |
| Product images (Cloudflare R2) | **Faked** — placeholder PNGs served from `src/Maxkeys.Api/wwwroot/products/` with `Storage:R2PublicBaseUrl=http://localhost:8080`. |

Everything demo-related is inert outside the `Development` environment: `Payments:Mode=Fake`
is ignored and the `/dev/*` routes are not mapped when `ASPNETCORE_ENVIRONMENT` is anything
else, so a production deployment cannot enable it by configuration.

## Quickstart

All defaults live in `src/Maxkeys.Api/appsettings.Development.json`, which is only loaded in
the `Development` environment. `Properties/launchSettings.json` is not committed, so pass
`--environment Development` explicitly (or export `ASPNETCORE_ENVIRONMENT=Development`). Run
every command from the repo root; note that `dotnet run --project` executes with the project
directory as working directory, hence the absolute seed path (`$PWD` works in bash and
PowerShell).

```bash
# 1. Database
docker compose up -d                    # postgres:16-alpine on localhost:5432, maxkeys/maxkeys/maxkeys

# 2. Schema and catalog
dotnet run --project src/Maxkeys.Api -- --environment Development --migrate
dotnet run --project src/Maxkeys.Api -- --environment Development --seed-catalog "$PWD/seed/catalog.json"
# --seed-catalog is bootstrap-only once the admin catalog UI exists: it upserts by slug and
# overwrites any edits an admin made through /admin/catalog/*. Re-run it only to add new
# products, not to refresh existing ones.

# 3. API on http://localhost:8080 (the frontend's default API base URL)
dotnet run --project src/Maxkeys.Api -- --environment Development --urls http://localhost:8080
```

Frontend, in the sibling repo (`../maxkeys-front`):

```bash
# .env — NUXT_PUBLIC_API_BASE_URL already defaults to http://localhost:8080
NUXT_PUBLIC_API_BASE_URL=http://localhost:8080
NUXT_PUBLIC_SITE_URL=http://localhost:3000
# plus the Supabase URL/anon key of your project for login

npm install
npm run dev                             # http://localhost:3000, already in the API's Cors:AllowedOrigins
```

Sanity check before opening the browser:

```bash
curl http://localhost:8080/health
curl http://localhost:8080/catalog/products          # imageUrl values point at http://localhost:8080/products/*.png
curl -I http://localhost:8080/products/steam-wallet-gift-card.png   # 200 image/png
```

When you are done: `docker compose down` (add `-v` to drop the database volume).

## Demo walkthrough

### 1. Browse and add to cart

Open `http://localhost:3000`, pick a product, choose a variant, add it to the cart. Product
images are the placeholder PNGs.

### 2. Checkout

Fill in the buyer email (guest) or log in first with Supabase (the order is then linked to
your user and shows up in "Mis compras"). Submitting the cart calls `POST /checkout/orders`;
in demo mode the response's `initPoint` is `http://localhost:8080/dev/payments/{orderId}` and
the frontend redirects there exactly as it would to Mercado Pago.

Equivalent `curl` (variant id from `GET /catalog/products/{slug}`):

```bash
curl -s -X POST http://localhost:8080/checkout/orders \
  -H "Content-Type: application/json" \
  -d '{"email":"buyer@example.com","items":[{"variantId":"<VARIANT_ID>","quantity":1}]}'
# -> {"orderId":"<ORDER_ID>","initPoint":"http://localhost:8080/dev/payments/<ORDER_ID>"}
```

### 3. Fake payment page and approval

`GET /dev/payments/{orderId}` shows the order id, buyer, amount and current status, plus an
**Approve payment** button. Clicking it does `POST /dev/payments/{orderId}/approve`, which:

1. registers an approved payment for the order in `FakePaymentGateway` (same external
   reference, amount and currency as the order);
2. calls `ProcessPaymentNotification.ExecuteAsync(requestId, paymentId)` with a synthetic
   request id `dev-{orderId}-{guid}` — the order becomes `Paid` and an `OrderApproved` outbox
   event is written, exactly like a real webhook;
3. redirects (303) to `Payments:FakeReturnUrl`, by default
   `http://localhost:3000/checkout/result?orderId={orderId}&status=approved` — the same
   query parameters the Mercado Pago back URL carries, so the result page polls
   `GET /checkout/orders/{id}/status` and shows "Pago aprobado".

```bash
curl -i -X POST http://localhost:8080/dev/payments/<ORDER_ID>/approve     # HTTP/1.1 303, Location: ...
curl http://localhost:8080/checkout/orders/<ORDER_ID>/status               # "status":"Paid" or "AwaitingFulfillment"
```

Within a few seconds the outbox processor picks the event up, moves the order to
`AwaitingFulfillment` and "sends" the operator email — look for
`Email to operator@localhost: ...` in the API console.

### 4. Fulfil the order (admin)

Key attachment is a real admin endpoint, so it needs a Supabase user whose `sub` is listed in
`Auth:AdminSubs` (`Auth__AdminSubs__0=<supabase-user-id>` as an environment variable, or edit
`appsettings.Development.json`) and that user's access token as `<ADMIN_JWT>`. Item ids come
from the admin queue:

```bash
curl -H "Authorization: Bearer <ADMIN_JWT>" http://localhost:8080/admin/orders
# -> [{"id":"<ORDER_ID>","status":"AwaitingFulfillment","items":[{"itemId":"<ITEM_ID>","quantity":1,"assigned":0,...}]}]

curl -X POST http://localhost:8080/admin/orders/<ORDER_ID>/items/<ITEM_ID>/keys \
  -H "Authorization: Bearer <ADMIN_JWT>" \
  -H "Content-Type: application/json" \
  -d '{"code":"DEMO-KEY-0001-ABCD"}'
# -> {"orderStatus":"Delivered","items":[{"itemId":"<ITEM_ID>","quantity":1,"assigned":1}]}   once every unit has a key
```

Attach one key per unit (an item with `quantity: 2` needs two calls). When the last key lands
the order becomes `Delivered`, an `OrderDelivered` outbox event fires and the buyer's delivery
email is printed to the API console (subject and recipient at `Information`; the body, which
never contains the keys, at `Debug`).

### 5. Keys in "Mis compras"

If the buyer checked out while logged in, open `http://localhost:3000/account/orders` — the
order is listed as delivered and `GET /me/orders/{id}` returns the decrypted keys. A guest order
is not linked to any user, so it will not appear there; that is the production behaviour.

## Configuration reference

| Key | Demo default | Purpose |
|-----|--------------|---------|
| `ConnectionStrings:Default` | `Host=localhost;Port=5432;Database=maxkeys;Username=maxkeys;Password=maxkeys` | The compose Postgres. |
| `Payments:Mode` | `Fake` | Selects `FakePaymentGateway` (Development only). Empty restores the production selection: Mercado Pago when `Payments:AccessToken` is set, otherwise the not-configured gateway (503 on checkout). |
| `Payments:FakeInitPointBaseUrl` | `http://localhost:8080` | Base of the fake init point handed to the frontend. |
| `Payments:FakeReturnUrl` | `http://localhost:3000/checkout/result?orderId={orderId}&status=approved` | Where the approval redirects; `{orderId}` is substituted. |
| `Email:Sender` | `Logging` | Emails go to the console. |
| `Storage:R2PublicBaseUrl` | `http://localhost:8080` | `ImageUrlBuilder` joins this with the product `imageKey` (`products/<slug>.png`), which resolves to `wwwroot/products/<slug>.png` via static files. |
| `Cors:AllowedOrigins` | `["http://localhost:3000"]` | The Nuxt dev server origin. |
| `Auth:*` | unchanged placeholders | Fill in your Supabase issuer/JWKS URL to log in; not needed for guest checkout. |

Adding a product to `seed/catalog.json`? Drop a matching `wwwroot/products/<imageKey>.png`
next to the existing placeholders and re-run `--seed-catalog`.
