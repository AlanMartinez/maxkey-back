# Exploration: MVP Marketplace (Nexo) — digital game-keys / top-up storefront

Change: `mvp-marketplace` · Project: `maxkeys` · Date: 2026-09-12 · Engram topic: `sdd/mvp-marketplace/explore`

## Current State

Greenfield repo (`C:\Personal\Projects\maxkeys`), no git, no source code. Only two real artifacts exist:

1. `mock ui/Nexo Marketplace - Standalone.html` — a bundled, self-executing single-file mock (React-like component compiled into a template-string renderer; not a runnable Nuxt/React source tree, just a visual prototype).
2. `openspec/config.yaml` — already encodes the fixed stack, layering rules, and review policy (auto-chain, 400-line budget, payments/auth flagged high-risk).

This is the first phase for this change.

## Mock UI Findings

- **Brand**: "Nexo". Dark theme, `--bg:#0A0A0E`, purple accent `--accent:#7C5CFC` / hover `#8F6FFF`, success green `#22D3A8`. Headings in "Space Grotesk", body in "Inter". Glassmorphism (`backdrop-filter: blur`), rounded-xl cards, subtle borders — premium/trustworthy tone.
- **Views present** (only 3, driven by a single `view` state field): `landing` (catalog + hero carousel), `product` (detail + region/tier selector + reviews), `checkout` (contact/email + payment method badge "MERCADO PAGO" + pay button). Nav bar: Catálogo / Ofertas / Ayuda, search input, cart icon with count badge.
- **Catalog**: filter chips `['Todos','Roblox','EA','Riot Games','Rockstar','Steam']` (filter by **platform**). Product fields: `id, name, platform, price, priceNum, oldPrice, discount, imgId`. Items are **top-up currencies / cash cards** (Robux, FC 25 Points, Riot Points, GTA$ Shark Card), not literal game keys — same delivery model (digital code, region-bound).
- **Product/region/tier selector**: `REGIONS = ['LAS','LAN','NA','EUW','BR']`, `TIERS` = price/quantity tiers for the Riot Points product only. Mock equivalent of the brief's "selector región/edición si aplica", not generalized.
- **Trust copy**: "Entrega instantánea / automática en minutos", "Keys 100% verificadas", "Compra verificada".
- **Cart**: a full **persistent multi-item cart** (add, qty +/-, remove, subtotal, empty state) feeding into checkout.
- **Checkout**: "Contacto" section with `<input type="email">` — matches the brief's "always ask for email". Payment state defaults to `'mp'` (Mercado Pago only).
- **Gaps — mock has NO**: login/Google OAuth control; "Mis compras" / order-history view; post-payment confirmation/success screen; key-reveal/copy UI; loading, error, or skeleton states (only cart-empty exists). These must be designed net-new.
- **Gap — brief vs mock direction**: the brief's checkout description reads as **single-product-per-order**, but the mock is built around a **multi-item cart**. Real product-scope fork — see Decision F.

## Domain Concepts & Candidate Entities (options only, not finalized)

- `Product` — game/service, platform, base info, images (R2 keys).
- `ProductVariant` (optional) — region/edition/tier combination with its own price and key pool. Alternative: flat extra `Product` rows per variant (simpler, more rows, catalog duplication).
- `Key` — encrypted secret, `Status` (Available/Reserved/Delivered), FK to `Product`/`ProductVariant`.
- `Order` — `BuyerEmail` (always), `UserId` (nullable, Supabase `sub`), `Status` enum, `ExternalReference` (= order id sent to MP), totals.
- `OrderItem` (optional) — only if cart/multi-item is adopted; otherwise `Order` holds a direct `ProductId`/`ProductVariantId` FK.
- `OutboxEvent` — `Type` (e.g. `OrderApproved`), `Payload`, `Status` (Pending/Processing/Processed/Failed), `Attempts`, `NextAttemptAt`, written in the SAME transaction as the order-state update.
- `ProcessedWebhookNotification` — dedupe table keyed by MP `data.id`/`payment_id` (or `topic`+`id`).
- `User` link — no local `Users` table; store Supabase `sub` (UUID) as `Order.UserId`. Profile data stays in Supabase Auth.

## Design Decisions Requiring Trade-off Discussion

| # | Decision | Options | Trade-off summary |
|---|---|---|---|
| a | Modeling "awaiting manual fulfillment" | (1) Extra `Order.Status` value `AwaitingManualFulfillment` vs (2) separate `Fulfillment` entity (1:1 with Order) with own status/assignee/notes | (1) simpler, fewer joins, no premature abstraction; risks overloading Order later. (2) isolates fulfillment concerns and gives a home for future auto-assignment/audit trail at the cost of one more table now. Either way the **same `OrderApproved` OutboxEvent** is the trigger; auto-assignment later only changes the event *handler* — no outbox schema change. |
| b | Key encryption at rest | (1) ASP.NET Core Data Protection (needs durable key-ring store across instances) vs (2) AES-GCM with key from env var/user-secrets vs (3) pgcrypto (`pgp_sym_encrypt`) | (1) idiomatic .NET but needs a persistent `IXmlRepository` (R2/DB) for multi-instance Fly.io/Railway. (2) most legible for a single sensitive column; manual rotation. (3) keeps .NET simple but couples schema to a Postgres extension and moves key management to the DB. All three must guarantee keys are **never logged** (Serilog destructuring/masking) and decrypted only at display/email time. |
| c | Webhook idempotency + signature validation | Idempotency: (1) dedupe table on notification/payment id vs (2) order-state guard vs (3) both. Signature: MP sends `x-signature: ts=<ts>,v1=<sig>` + `x-request-id`; verify by building `id:<data.id>;request-id:<x-request-id>;ts:<ts>;`, HMAC-SHA256 with the webhook secret, constant-time compare to `v1`. | (3) both recommended: dedupe table protects against replay before touching state; state guard protects against out-of-order/manual retries. Signature validation is mandatory before any state mutation — reject early. |
| d | Guest vs authenticated orders | Nullable `Order.UserId` + `Order.BuyerEmail` always required | Low ambiguity. Open detail: if a logged-in user edits the prefilled email, still link `UserId`? Recommend yes — email is contact info, `UserId` is the link. |
| e | Outbox polling via `IHostedService` | Claiming: `UPDATE ... SET status='Processing' WHERE id=... AND status='Pending'` vs `SELECT ... FOR UPDATE SKIP LOCKED`. Backoff: fixed vs exponential with `NextAttemptAt`. Poison events: cap `Attempts`, move to `Failed`. | Deploy target may run >1 instance, so `FOR UPDATE SKIP LOCKED` is the safer default without adding an external queue. |
| f | Single product per order vs cart | (1) Single-item orders, simplified checkout (drop mock's cart) vs (2) cart UX fanning out to N `Order`+`OutboxEvent` rows at checkout vs (3) `OrderItem` multi-item orders | **Business/product decision** — needs explicit answer before proposal scopes checkout. |
| g | Mercado Pago Checkout Pro specifics | One item per preference, `external_reference = order.Id`, `back_urls` (success/failure/pending) + `auto_return: approved`, `notification_url` → `POST /webhooks/mercadopago`. Sandbox via MP test users/cards; separate `MP_ACCESS_TOKEN`/`MP_PUBLIC_KEY` per env. | Standard; low ambiguity once (f) is resolved. |

## Folder Structure Candidates

**Backend** (layered; Domain has no dependency on Infrastructure/API):

```
src/
  Maxkeys.Domain/          # entities, value objects, enums, domain events — no EF/ASP.NET refs
  Maxkeys.Application/     # use cases/services, DTOs, interfaces consumed by Infrastructure
  Maxkeys.Infrastructure/  # EF Core DbContext, migrations, R2 client, MP client, outbox processor
  Maxkeys.Api/             # endpoints, JWT/JWKS middleware, Problem Details, DI wiring
tests/
  Maxkeys.Domain.Tests/
  Maxkeys.Application.Tests/   # webhook idempotency + state-transition tests
```

Per "no interfaces without a second real implementation", Application should only define interfaces for things Infrastructure genuinely swaps (e.g. `IKeyEncryptor`, `IPaymentGateway`) — not blanket repository interfaces.

**Frontend** (Nuxt 3 conventions):

```
frontend/
  components/  # ProductCard, RegionSelector, CartDrawer, OrderStatusBadge, KeyReveal
  composables/ # useAuth(), useCart() (if cart adopted), useCheckout()
  pages/       # index.vue (catalog), product/[id].vue, checkout.vue, account/orders.vue
  server/      # only if a BFF/proxy is needed; likely thin
```

## Approaches — cart-adoption fork (Decision f)

1. **Single-product buy-now** — drop the mock's cart; "Comprar ahora" goes straight to checkout.
   - Pros: matches brief literally, smallest backend surface, fastest end-to-end slice.
   - Cons: discards mock cart UX. Effort: Low.
2. **Keep cart UX, fan out to N single-product orders at checkout.**
   - Pros: preserves mock UX, backend stays one-product-per-order.
   - Cons: multi-order reconciliation (1 vs N confirmations, partial failures); MP Checkout Pro wants one preference per purchase. Effort: Medium.
3. **Full cart + `OrderItem`.**
   - Pros: natural fit for mock, most extensible.
   - Cons: not requested; per-item fulfillment/status complexity. Effort: High.

## Recommendation

Lead with **Option 1** for the MVP backend/data model — it is what the brief describes end-to-end. Treat the mock's cart as a visual reference, not a contract; flag explicitly to the user as a scope decision. Defaults to confirm: AES with env-var key (b-2); `SELECT ... FOR UPDATE SKIP LOCKED` for outbox claiming; dedupe table + state guard + mandatory signature validation for the webhook.

## Open Questions

**Business/product** (before proposal):
1. Cart vs single-product-per-order (Decision f).
2. Logged-in user edits email at checkout — still link `UserId`?
3. "Mis compras" logged-in only? (Brief says no guest lookup page — confirming.)

**Technical** (defaults proposed, confirm in design):
4. Key encryption mechanism.
5. Fulfillment modeling (extra status vs `Fulfillment` entity).
6. Outbox claim strategy.
7. Confirm the Supabase project uses **asymmetric JWT signing keys** (JWKS at `/auth/v1/.well-known/jwks.json`) rather than legacy shared-secret HS256 — JWKS validation only works with asymmetric keys.

## Risks

- **Payments (high-risk)**: bad signature validation or missing idempotency → double fulfillment or orders stuck in `Pending`. MP sandbox must be exercised before go-live.
- **Auth (high-risk)**: JWKS validation depends on Supabase signing-key mode (Q7).
- **Scope fork**: cart vs single-product touches mock UX and backend order model — resolve before proposal.
- **No source code exists** — exploration is about shape, nothing has run.

## Ready for Proposal

Yes, with Decision f and Q7 surfaced to the user before/during proposal.

## Sources

- Mercado Pago webhook signature validation — https://www.mercadopago.cl/developers/en/docs/checkout-pro-preferences/payment-notifications , https://github.com/mercadopago/sdk-nodejs/discussions/318
- Supabase JWKS / asymmetric JWT signing keys — https://supabase.com/docs/guides/auth/jwts , https://supabase.com/blog/jwt-signing-keys
