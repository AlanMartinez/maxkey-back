# Design: MVP Marketplace (Nexo)

> Size note: this design exceeds the usual 800-word budget on purpose. The change is a greenfield system touching payments and auth (both flagged high-risk in `openspec/config.yaml`), and the orchestrator asked for sequence diagrams, a configuration matrix, and ADRs before any code exists. Sections are ordered so a reviewer can stop after "Architecture at a glance" and "ADRs" and still know every decision.

## 1. Context, goals, non-goals

**Context.** Greenfield repo. Inputs: `mock ui/Nexo Marketplace - Standalone.html` (visual identity + cart UX), `openspec/config.yaml` (fixed stack, layering rule, 400-line review budget, auto-chain), and the accepted proposal (`proposal.md`). All binding decisions from the proposal are taken as given and are not reopened here.

**Goals (this design must enable)**

| Goal | How this design delivers it |
|---|---|
| Buyer pays once for N items via Mercado Pago and receives all keys together | `Order 1-N OrderItem`, one MP preference, all-or-nothing `Delivered` derived from key counts |
| Payment confirmation is idempotent and replay-safe | Signature check before I/O, `ProcessedWebhookNotification` dedupe, state guard, outbox insert in the same transaction |
| Manual fulfillment can be swapped for automatic assignment later | `Paid` and `AwaitingFulfillment` stay separate; the swap is one outbox handler |
| Plaintext keys never leak | AES-256-GCM at rest, decrypt in exactly two code paths, Serilog destructuring policy |
| Reviewable in 400-line PRs | Layering and slice boundaries in section 13 |

**Non-goals.** Everything in the proposal's "Out of Scope": automatic assignment, admin UI, bulk key loading, guest order lookup, refunds/chargebacks (fully out — no flagging logic either), expiry of `Pending` orders, server-side cart, catalog management UI.

## 2. Architecture at a glance

```
             ┌──────────────────────────┐   HTTPS (Bearer = Supabase access token)
             │  maxkeys-front (Nuxt 3)  │──────────────────────────────┐
             │  @nuxtjs/supabase, Tailwind│                              │
             └──────────┬───────────────┘                              ▼
                        │ OAuth (Google)                  ┌───────────────────────────┐
                        ▼                                 │ Maxkeys.Api               │
             ┌──────────────────────────┐                 │ minimal APIs, JWT, policy │
             │ Supabase Auth            │◄── JWKS fetch ──│ Problem Details, Serilog  │
             └──────────────────────────┘                 └─────────────┬─────────────┘
                                                                        │ calls use cases
   ┌──────────────┐  POST webhook / GET payment / create preference     ▼
   │ Mercado Pago │◄───────────────────────────────┐     ┌───────────────────────────┐
   └──────────────┘                                │     │ Maxkeys.Application       │
                                                   │     │ use cases (plain classes) │
   ┌──────────────┐  SMTP                          │     │ IAppDbContext, IPaymentGw,│
   │ Email (SMTP) │◄──────────────────────┐        │     │ IEmailSender, IOutboxHandler│
   └──────────────┘                       │        │     └─────────────┬─────────────┘
                                          │        │                   │ uses entities
   ┌──────────────┐  public image URLs    │        │                   ▼
   │ Cloudflare R2│◄────(browser only)    │        │     ┌───────────────────────────┐
   └──────────────┘                       │        │     │ Maxkeys.Domain            │
                                          │        │     │ entities, enums, rules    │
                                          │        │     │ no framework references   │
                                          │        │     └───────────────────────────┘
                                          │        │                   ▲ implements
                                          │        │                   │
                                          └────────┴─────┌───────────────────────────┐
                                                         │ Maxkeys.Infrastructure    │
   ┌──────────────┐  Npgsql / EF Core                     │ AppDbContext, migrations, │
   │ Postgres     │◄──────────────────────────────────────│ OutboxProcessor (hosted), │
   │ (Supabase)   │                                       │ MP gateway, SMTP sender   │
   └──────────────┘                                       └───────────────────────────┘
```

**Dependency direction (compile-time):** `Api → Application → Domain`; `Api → Infrastructure → Application → Domain`. Domain references nothing but the BCL. Application references `Microsoft.EntityFrameworkCore` only for `DbSet<T>` in `IAppDbContext`; it never references Infrastructure. Infrastructure references Npgsql, MailKit, and the Application seams it implements. Api wires DI.

**Runtime shape.** One process hosts both the HTTP API and the outbox poller (`IHostedService`). Multiple instances are safe: the poller claims with `FOR UPDATE SKIP LOCKED`, the webhook dedupes on a primary key, and `Order` carries an `xmin` concurrency token.

## 3. Solution layout

Two repositories (ADR-18, user decision 2026-09-12). The proposal's backend tree is confirmed with four adjustments (rationale inline); the frontend tree is the proposal's `frontend/` subtree promoted to the root of its own repository.

| Repository | GitHub | Local folder | Owns |
|---|---|---|---|
| `maxkeys-back` | `https://github.com/AlanMartinez/maxkey-back.git` | `C:\Personal\Projects\maxkeys-back` | `openspec/` (all SDD artifacts), `Maxkeys.sln`, `src/`, `tests/`, `seed/`, `Dockerfile`, `deploy/` (`fly.toml`, `railway.json`). `main` exists (3 commits) |
| `maxkeys-front` | `https://github.com/AlanMartinez/maxkey-front.git` | `C:\Personal\Projects\maxkeys-front` | Nuxt 3 app at the repo root. No `openspec/` copy; its README links to `maxkeys-back/openspec/changes/mvp-marketplace/` (specs + this design) as the contract source. `git init -b main` + initial commit happen in the first frontend slice (PR13) |

### Backend repository (`maxkeys-back`)

```
maxkeys-back/
├── openspec/                             # SDD artifacts (proposal, specs, design, tasks) — single source of truth for both repos
├── Maxkeys.sln
├── Directory.Build.props                 # net8.0, nullable, implicit usings, TreatWarningsAsErrors
├── src/
│   ├── Maxkeys.Domain/
│   │   ├── Catalog/    Product.cs, ProductVariant.cs
│   │   ├── Orders/     Order.cs, OrderItem.cs, OrderStatus.cs
│   │   ├── Keys/       Key.cs, KeyStatus.cs
│   │   ├── Outbox/     OutboxEvent.cs, OutboxEventStatus.cs, OutboxEventTypes.cs
│   │   ├── Payments/   ProcessedWebhookNotification.cs           # (+) lived nowhere in the proposal tree
│   │   └── Common/     Entity.cs, DomainException.cs, DomainConflictException.cs  # (+) 422 vs 409 mapping (section 7)
│   ├── Maxkeys.Application/
│   │   ├── Catalog/        GetCatalog.cs, GetProductBySlug.cs, CatalogDtos.cs
│   │   ├── Checkout/       CreateOrder.cs, GetOrderStatus.cs      # (+) result-page polling endpoint
│   │   ├── Payments/       ProcessPaymentNotification.cs, IPaymentGateway.cs, PaymentGatewayException.cs
│   │   ├── Outbox/         IOutboxHandler.cs, OrderApprovedHandler.cs, OrderDeliveredHandler.cs  # (+) see ADR-04
│   │   ├── Fulfillment/    AttachKeyToOrderItem.cs, ListOrdersAwaitingFulfillment.cs
│   │   ├── Orders/         GetMyOrders.cs, GetMyOrder.cs
│   │   ├── Security/       KeyCipher.cs, KeyCipherOptions.cs
│   │   ├── Notifications/  IEmailSender.cs, EmailMessage.cs, EmailTemplates.cs
│   │   ├── Persistence/    IAppDbContext.cs
│   │   └── Common/         NotFoundException.cs
│   ├── Maxkeys.Infrastructure/
│   │   ├── Persistence/    AppDbContext.cs, Configurations/*.cs, Migrations/, CatalogSeeder.cs  # (+) ADR-12
│   │   ├── Outbox/         OutboxProcessor.cs, OutboxOptions.cs, OutboxClaimQuery.cs
│   │   ├── Payments/       MercadoPagoGateway.cs, MercadoPagoOptions.cs, MercadoPagoSignatureValidator.cs,
│   │   │                   NotConfiguredPaymentGateway.cs   # (+) registered when Payments:AccessToken is empty; throws PaymentGatewayException (503)
│   │   ├── Storage/        R2ImageUrlResolver.cs, StorageOptions.cs
│   │   ├── Email/          SmtpEmailSender.cs, LoggingEmailSender.cs, EmailOptions.cs
│   │   └── DependencyInjection.cs
│   └── Maxkeys.Api/
│       ├── Endpoints/      CatalogEndpoints.cs, CheckoutEndpoints.cs, WebhookEndpoints.cs, AdminEndpoints.cs, MeEndpoints.cs, HealthEndpoints.cs
│       ├── Auth/           AuthOptions.cs, JwtSetup.cs, JwksKeyCache.cs, AdminPolicy.cs, OptionalBearerFilter.cs
│       ├── Errors/         ProblemDetailsExceptionHandler.cs
│       ├── Logging/        SerilogSetup.cs, CorrelationIdMiddleware.cs, SensitiveDataPolicy.cs
│       ├── Program.cs, appsettings.json, appsettings.Development.json, Dockerfile
├── tests/
│   ├── Maxkeys.Domain.Tests/
│   ├── Maxkeys.Application.Tests/       # Testcontainers Postgres fixture lives here
│   └── Maxkeys.Api.Tests/               # WebApplicationFactory + same fixture
├── deploy/                              # fly.toml, railway.json (skeletons)
└── seed/catalog.json                    # operator-maintained catalog seed (ADR-12)
```

### Frontend repository (`maxkeys-front`)

```
maxkeys-front/                # Nuxt 3 app at the repo ROOT — no frontend/ prefix
├── README.md                 # links to maxkeys-back/openspec/changes/mvp-marketplace/ (specs, design section 7) as the API contract source
├── nuxt.config.ts            # modules: @nuxtjs/supabase (redirect: false), @nuxtjs/tailwindcss; runtimeConfig.public.apiBaseUrl, siteUrl
├── tailwind.config.ts        # tokens from the mock (section 9)
├── assets/css/main.css       # font imports (Space Grotesk, Inter), base glass utilities
├── app.vue                   # AppHeader + <NuxtPage> + CartDrawer + LoginDialog
├── components/
│   ├── layout/    AppHeader.vue, AppFooter.vue, LoginDialog.vue
│   ├── catalog/   HeroCarousel.vue, PlatformFilter.vue, ProductCard.vue, ProductGrid.vue
│   ├── product/   VariantSelector.vue, TrustBadges.vue
│   ├── cart/      CartDrawer.vue, CartLine.vue
│   ├── checkout/  ContactForm.vue, OrderSummary.vue, PayWithMercadoPago.vue
│   ├── orders/    OrderCard.vue, OrderStatusBadge.vue, KeyReveal.vue
│   └── ui/        AppButton.vue, AppBadge.vue, Skeleton.vue, EmptyState.vue, ErrorState.vue
├── composables/   useApi.ts, useAuth.ts, useCart.ts, useCheckout.ts
├── middleware/    auth.ts                # applied per page via definePageMeta({ middleware: 'auth' })
├── pages/         index.vue, product/[slug].vue, checkout/index.vue, checkout/result.vue,
│                  account/orders/index.vue, account/orders/[id].vue, auth/callback.vue
├── types/api.ts   # DTOs mirrored by hand from design section 7 — the cross-repo contract (ADR-18)
├── tests/         # Vitest: useCart.spec.ts, useCheckout.spec.ts, VariantSelector.spec.ts
└── .env.example   # NUXT_PUBLIC_API_BASE_URL, NUXT_PUBLIC_SITE_URL, SUPABASE_URL, SUPABASE_KEY
```

## 4. Domain model

### 4.1 Entities and invariants

| Entity | Invariants enforced in the constructor/factory | Transition methods |
|---|---|---|
| `Product` | `Slug` non-empty, lowercase, unique (DB); `Name`, `Platform` non-empty; at least one variant before it can be active (checked by seeder, not entity — variants are added after construction) | none in MVP (catalog is seeded) |
| `ProductVariant` | `Price > 0`; `OldPrice` null or `> Price`; `Currency == "ARS"` | none |
| `Order` | >= 1 item, <= 20 items; `BuyerEmail` present and parses as a mail address; `TotalAmount == sum(UnitPrice * Quantity)` (computed, never accepted from outside) | `Create` (factory), `AttachPreference`, `RecordPaymentAttempt`, `MarkPaid`, `MarkAwaitingFulfillment`, `AttachKey`, `Cancel` |
| `OrderItem` | `Quantity` in 1..10; snapshots non-empty | `IsComplete` (derived: `Keys.Count(k => k.Status == Assigned) == Quantity`) |
| `Key` | `EncryptedCode` non-empty; `KeyVersion >= 1`; `LoadedBy` non-empty | `AssignTo(orderItemId, now)`: `Available → Assigned`, sets `AssignedAt` |
| `OutboxEvent` | `Type` in `OutboxEventTypes`; `Payload` valid JSON | `Claim(leaseUntil)`, `MarkProcessed(now)`, `MarkFailedAttempt(error, now, maxAttempts)` |
| `ProcessedWebhookNotification` | `RequestId` non-empty | none (insert-only) |

`Entity` base: `Guid Id` (client-generated `Guid.NewGuid()`, so ids exist before the first `SaveChanges` — needed for `external_reference`). No domain-event infrastructure; use cases read the resulting state.

### 4.2 `Order` state machine

```
Pending ──MarkPaid(paymentId)──► Paid ──MarkAwaitingFulfillment()──► AwaitingFulfillment ──AttachKey (last key)──► Delivered
   │
   └──Cancel()──► Cancelled        (no automatic trigger in MVP; method exists for a later cleanup change)
```

| Method | Precondition | Effect | Failure |
|---|---|---|---|
| `Order.Create(userId?, email, lines[(product, variant, qty)], now)` | lines valid, variants active | builds items with snapshots, computes total, `Status = Pending` | `DomainException` (422) |
| `AttachPreference(preferenceId)` | `Pending`, `MpPreferenceId == null` | sets preference id | `DomainConflictException` (409) |
| `RecordPaymentAttempt(paymentId, mpStatus, now)` | `Pending` | `LastPaymentAttemptId = paymentId`, `LastPaymentAttemptStatus = mpStatus` (`rejected` / `pending` / `in_process`), `LastPaymentAttemptAt = now`; **no status change** | `DomainConflictException` (order not `Pending`) |
| `MarkPaid(paymentId, now)` | `Pending` | `Status = Paid`, `PaidAt`, `MpPaymentId = paymentId`, and the three `LastPaymentAttempt*` fields with `approved` | `DomainConflictException` |
| `MarkAwaitingFulfillment()` | `Paid` | `Status = AwaitingFulfillment` | `DomainConflictException` |
| `AttachKey(orderItemId, key, now)` | `AwaitingFulfillment`; item exists; `!item.IsComplete`; `key.ProductVariantId == item.ProductVariantId`; `key.Status == Available` | `key.AssignTo(item.Id)`, appends to `item.Keys`, bumps `UpdatedAt`; if every item `IsComplete` → `Status = Delivered`, `DeliveredAt = now` | `DomainConflictException` (wrong status, **item already has `Quantity` keys**), `DomainException` (variant mismatch / item missing) |
| `Cancel()` | `Pending` | `Status = Cancelled` | `DomainConflictException` |

Two exception types, one rule: `DomainConflictException : DomainException` means "the request is valid but the current state of the order does not allow it" (wrong status, item already full, concurrency loser) and maps to 409; plain `DomainException` means "the request violates a business rule regardless of state" (inactive variant, wrong variant for the item, invalid email, quantity out of range) and maps to 422.

`MpPaymentId` remains the id of the **approved** payment only (it carries the partial unique index). Rejected or pending attempts are visible through the `LastPaymentAttempt*` fields so the operator (and the result page) can see that a buyer tried and failed, without ever leaving `Pending`.

The domain methods are strict (throw on wrong state). Use cases that must be idempotent (webhook, outbox handlers) check the state *before* calling and treat "already past this state" as a logged no-op. This keeps the entity honest and the idempotency decision visible in one place per use case.

### 4.3 Concurrency

- `Order` maps Postgres `xmin` as the concurrency token (`UseXminAsConcurrencyToken()`).
- `AttachKey` always bumps `Order.UpdatedAt` (new column vs proposal) so that two concurrent attaches on the same order *always* produce an `Order` row update and therefore a conflict. Without it, two attaches that do not change `Status` would both succeed on stale in-memory key counts and could both miss the "last key" check.
- `AttachKeyToOrderItem` and `ProcessPaymentNotification` catch `DbUpdateConcurrencyException`, reload, re-run the domain call **once**, then rethrow as `DomainConflictException` (409) if it conflicts again. On the retry the reloaded state is authoritative: if the item is now full, `AttachKey` itself throws `DomainConflictException`, so the losing operator sees 409 either way. One retry is enough for a single-operator system; a loop would hide bugs.

### 4.4 Delivery derivation

`Delivered` is never set directly. It is the result of `AttachKey` observing `Items.All(i => i.IsComplete)`. There is no item status column: the key rows are the single source of truth, so nothing can drift.

## 5. EF Core mapping

| Concern | Decision | Rationale |
|---|---|---|
| Naming | `EFCore.NamingConventions` snake_case (`outbox_events`, `next_attempt_at`) | The claim query and any operator SQL read naturally; no quoting |
| Enums | stored as `text` via `HasConversion<string>()` + `HasMaxLength(32)` | Readable in psql during manual operations; adding a value needs no migration. Cost: a few bytes per row, irrelevant at MVP volume |
| Money | `numeric(12,2)` | Exact; ARS amounts fit |
| Timestamps | `DateTimeOffset` → `timestamptz`, always UTC via `TimeProvider` | Avoids Npgsql's `DateTime.Kind` pitfalls |
| `Key.EncryptedCode` | `bytea` (`byte[]`), layout `nonce(12) \| tag(16) \| ciphertext` | Single column; version in `key_version smallint` |
| `OutboxEvent.Payload` | `jsonb` on a `string` property | Queryable in psql; the app treats it as a serialized DTO |
| `Order` concurrency | `xmin` via `UseXminAsConcurrencyToken()` | No extra column; Postgres maintains it |
| `Order` payment attempt | `last_payment_attempt_id text NULL`, `last_payment_attempt_status text NULL`, `last_payment_attempt_at timestamptz NULL` | Required by the payments-webhook spec (rejected/pending attempts are recorded, never transition). Separate from `mp_payment_id`, which holds only the approved payment |
| Owned collections | `Order.Items` and `OrderItem.Keys` as navigations with `.Include`; `DeleteBehavior.Restrict` everywhere | Keys and orders are never deleted (rollback plan) |

**Indexes and constraints**

| Table | Index / constraint | Why |
|---|---|---|
| `products` | `UNIQUE(slug)`, `INDEX(platform)` | slug lookup, platform filter |
| `product_variants` | `INDEX(product_id)` | product detail |
| `orders` | `INDEX(user_id)`, `INDEX(status)`, `UNIQUE(mp_payment_id) WHERE mp_payment_id IS NOT NULL` | Mis compras, admin list, one payment can never pay two orders |
| `order_items` | `INDEX(order_id)` | load order |
| `keys` | `INDEX(order_item_id)`, `INDEX(product_variant_id, status)` | completion count; future pool lookup |
| `outbox_events` | `INDEX(status, next_attempt_at)` | claim query |
| `processed_webhook_notifications` | `PRIMARY KEY(request_id)` | dedupe by PK violation, not by read-then-write |

**Migration strategy.** Additive only during MVP (new tables/columns/indexes; no renames or drops). One migration per slice that changes the schema, generated with `dotnet ef migrations add` into `Infrastructure/Persistence/Migrations`. Applied by `Maxkeys.Api --migrate` (section 12), never automatically on web startup.

## 6. Sequence diagrams

### (a) Checkout → order → MP preference → redirect → result page

```mermaid
sequenceDiagram
    autonumber
    participant B as Browser (Nuxt)
    participant API as Maxkeys.Api
    participant UC as CreateOrder
    participant DB as Postgres
    participant MP as Mercado Pago

    B->>API: POST /checkout/orders {email, items[{variantId, qty}]} (Bearer optional)
    API->>UC: Execute(request, userId?)
    UC->>DB: load active variants + products by ids
    UC->>UC: Order.Create(...) recompute total, snapshot names/prices
    UC->>DB: INSERT order + items (Pending)   -- commit #1
    UC->>MP: POST /checkout/preferences (N items, external_reference=orderId, back_urls, notification_url)
    MP-->>UC: {id, init_point}
    UC->>DB: UPDATE order SET mp_preference_id  -- commit #2
    UC-->>API: {orderId, initPoint}
    API-->>B: 201 Created
    B->>B: keep cart, store orderId in sessionStorage
    B->>MP: window.location = init_point
    MP-->>B: redirect back_url /checkout/result?orderId=..&status=approved|failure|pending
    loop every 3s, max 60s, until status != Pending
        B->>API: GET /checkout/orders/{id}/status
        API-->>B: {status, buyerEmailMasked, total}
    end
    B->>B: approved/Paid -> clear cart, show confirmation; failure -> keep cart, offer retry
```

Commit #1 happens before the MP call so that any preference MP ever creates points at a persisted order; if commit #2 fails, the preference still resolves through the webhook because `external_reference` is the order id. If the MP call fails, the order stays `Pending` without a preference and the client gets 503 (`PaymentGatewayException`).

### (b) Webhook intake

```mermaid
sequenceDiagram
    autonumber
    participant MP as Mercado Pago
    participant EP as WebhookEndpoints
    participant SV as SignatureValidator
    participant UC as ProcessPaymentNotification
    participant GW as MercadoPagoGateway
    participant DB as Postgres

    MP->>EP: POST /webhooks/mercadopago?data.id=P&type=payment  (x-signature, x-request-id)
    EP->>EP: Payments:WebhookEnabled == false ? -> 503 (MP retries)
    EP->>SV: Validate(ts, v1, data.id, x-request-id)  -- HMAC-SHA256, constant-time
    SV-->>EP: invalid -> 401, no I/O performed
    EP->>EP: type != payment -> 200 ignored
    EP->>UC: Execute(requestId, paymentId)
    UC->>DB: SELECT 1 FROM processed_webhook_notifications WHERE request_id = ?
    DB-->>UC: exists -> return Duplicate -> 200
    UC->>GW: GET /v1/payments/{paymentId}
    GW-->>UC: {status, external_reference, transaction_amount, currency}
    Note over UC: gateway failure -> PaymentGatewayException -> 503 (MP retries)
    UC->>DB: load order by external_reference (with xmin)
    alt order missing, or order not Pending (state guard), or approved with amount/currency mismatch
        UC->>DB: INSERT processed_webhook_notifications  -- record and move on
        UC-->>EP: Ignored(reason) -> 200 (logged at Warning with orderId/paymentId, never the body)
    else Pending and status in (rejected, pending, in_process)
        UC->>DB: BEGIN
        UC->>UC: order.RecordPaymentAttempt(paymentId, status, now)  -- stays Pending, no outbox row, never Cancelled
        UC->>DB: UPDATE orders (last_payment_attempt_*); INSERT processed_webhook_notifications
        UC->>DB: COMMIT
        UC-->>EP: AttemptRecorded -> 200 (Information log)
    else Pending and status not actionable (refunded, charged_back, cancelled, authorized, ...)
        UC->>DB: INSERT processed_webhook_notifications
        UC-->>EP: Ignored(status) -> 200 (generic ignored-status Warning log; refunds/chargebacks out of scope)
    else Pending and approved with matching amount/currency
        UC->>DB: BEGIN
        UC->>UC: order.MarkPaid(paymentId, now)
        UC->>DB: INSERT outbox_events (OrderApproved {orderId})
        UC->>DB: INSERT processed_webhook_notifications
        UC->>DB: COMMIT  (PK violation on request_id -> concurrent duplicate -> 200)
        UC-->>EP: Processed -> 200
    end
```

MP status handling in one table:

| Fetched `status` | Order `Pending` | Order past `Pending` |
|---|---|---|
| `approved` (amount + currency match) | `MarkPaid` + `OrderApproved` outbox row, same tx | state guard: dedupe row only, no-op |
| `approved` (mismatch) | dedupe row, `Error` log, no transition (operator investigates) | same |
| `rejected`, `pending`, `in_process` | `RecordPaymentAttempt` (fields only), dedupe row | dedupe row only |
| anything else (`refunded`, `charged_back`, `cancelled`, `authorized`, unknown) | dedupe row, generic ignored-status log | same |

Signature manifest per MP docs: `id:{data.id};request-id:{x-request-id};ts:{ts};` (omit a segment when its value is absent), HMAC-SHA256 with `Payments:WebhookSecret`, hex-compare against `v1` with `CryptographicOperations.FixedTimeEquals`. MP requires a 2xx within ~22s; the only outbound call is the payment fetch.

### (c) Outbox poller

```mermaid
sequenceDiagram
    autonumber
    participant HS as OutboxProcessor (IHostedService)
    participant DB as Postgres
    participant H as IOutboxHandler (OrderApproved)
    participant EM as IEmailSender

    loop every Outbox:PollIntervalSeconds
        HS->>DB: BEGIN
        HS->>DB: SELECT * FROM outbox_events WHERE status IN ('Pending','Processing') AND next_attempt_at <= now() ORDER BY created_at LIMIT :batch FOR UPDATE SKIP LOCKED
        HS->>DB: UPDATE claimed SET status='Processing', next_attempt_at = now() + lease
        HS->>DB: COMMIT
        loop each claimed event (sequential)
            HS->>H: HandleAsync(event)
            H->>DB: load order; if Paid -> MarkAwaitingFulfillment; SaveChanges
            H->>EM: operator email "Order X awaiting fulfillment" (items + quantities, no keys)
            alt success
                HS->>DB: UPDATE status='Processed', processed_at=now()
            else exception
                HS->>DB: attempts += 1; attempts >= 8 ? status='Failed' : status='Pending', next_attempt_at = now() + 30s * 2^attempts; last_error = message
            end
        end
    end
```

`Processing` rows whose lease (`next_attempt_at`) has expired are claimable again, which recovers from a crash between claim and completion without a separate `claimed_at` column. The handler is idempotent: if the order is already `AwaitingFulfillment` it skips the transition and only re-sends the operator email (a duplicate operator notification is acceptable; a lost one is not). `Failed` events are dead letters: visible via logs (`Error` level, includes event id) and by SQL; there is no automatic resurrection in MVP.

### (d) Admin attach key → all-or-nothing → Delivered → buyer email

```mermaid
sequenceDiagram
    autonumber
    participant A as Operator (HTTP client)
    participant API as AdminEndpoints (policy: Admin)
    participant UC as AttachKeyToOrderItem
    participant KC as KeyCipher
    participant DB as Postgres
    participant HS as OutboxProcessor
    participant H2 as OrderDeliveredHandler
    participant EM as IEmailSender

    A->>API: POST /admin/orders/{id}/items/{itemId}/keys {code}
    API->>UC: Execute(orderId, itemId, code, adminSub)
    UC->>DB: load order + items + keys (xmin)
    UC->>KC: Encrypt(code) -> (blob, version)
    UC->>UC: key = Key.Create(variantId, blob, version, loadedBy=adminSub); order.AttachKey(itemId, key, now)
    alt order.Status == Delivered (last key)
        UC->>DB: BEGIN; INSERT key; UPDATE order (Delivered, delivered_at, updated_at); INSERT outbox_events (OrderDelivered {orderId}); COMMIT
    else still AwaitingFulfillment
        UC->>DB: BEGIN; INSERT key; UPDATE order (updated_at); COMMIT
    end
    Note over UC,DB: DbUpdateConcurrencyException -> reload, retry once, then 409
    UC-->>API: {orderStatus, items[{itemId, quantity, assigned}]}
    API-->>A: 200
    HS->>H2: HandleAsync(OrderDelivered)
    H2->>DB: load order + items + keys; assert Delivered
    H2->>KC: Decrypt each key (in memory only)
    H2->>EM: one buyer email with all keys grouped by item
```

**Delivery-email guarantee, stated precisely:** one send per `Delivered` transition. The `OrderDelivered` row is inserted in the same transaction as `AwaitingFulfillment → Delivered`, which can happen only once per order, so the event exists exactly once. Handler retries may duplicate the email if the SMTP send succeeds but marking the event `Processed` fails (at-least-once). Note for sdd-tasks: carry a spec-wording clarification for the fulfillment requirement `One-Time Delivery Email` so it reads "one send per transition, at-least-once on retry" rather than strict exactly-once (ADR-04).

### (e) Auth

```mermaid
sequenceDiagram
    autonumber
    participant B as Browser (Nuxt)
    participant SB as Supabase Auth
    participant API as Maxkeys.Api
    participant JW as JwtBearer (JwtSetup)

    B->>SB: signInWithOAuth(google, redirectTo=/auth/callback)
    SB-->>B: redirect /auth/callback#access_token... ; @nuxtjs/supabase stores session
    B->>API: GET /me/orders  Authorization: Bearer <access_token>
    API->>JW: validate
    alt Auth:Mode == Jwks
        JW->>SB: GET /auth/v1/.well-known/jwks.json (cached 10 min; refetch on unknown kid, once)
        JW->>JW: verify signature (ES256/RS256), iss, aud=authenticated, exp
    else Auth:Mode == Hs256
        JW->>JW: verify HMAC with Auth:Hs256Secret, iss, aud, exp
    end
    JW-->>API: ClaimsPrincipal (sub, email, role) -- MapInboundClaims=false keeps "sub"
    API->>API: /admin/* -> policy Admin: sub in Auth:AdminSubs; empty allowlist => always deny
    API-->>B: 200 | 401 (Problem Details) | 403 (Problem Details)
```

`POST /checkout/orders` is anonymous but runs `OptionalBearerFilter`: if an `Authorization` header is present and the principal is *not* authenticated, respond 401 instead of silently creating a guest order with an expired token.

## 7. API contract

Base path `/`. All error bodies are RFC 7807 Problem Details (`application/problem+json`) with a `traceId` extension.

| Method + path | Auth | Request | Success response |
|---|---|---|---|
| `GET /catalog/products?platform=&q=` | public | query | `200 ProductSummary[]` `{id, slug, name, platform, imageUrl, fromPrice, oldPrice?}` |
| `GET /catalog/products/{slug}` | public | — | `200 ProductDetail` `{..., description, variants[{id, name, region?, edition?, price, oldPrice?, currency}]}` |
| `POST /checkout/orders` | public, optional bearer | `{email, items:[{variantId, quantity}]}` | `201 {orderId, initPoint}` |
| `GET /checkout/orders/{id}/status` | public (unguessable GUID) | — | `200 {orderId, status, lastPaymentAttemptStatus?, buyerEmailMasked, totalAmount, currency}` — `lastPaymentAttemptStatus` lets the result page distinguish "still Pending, payment rejected" from "still Pending, waiting for the webhook" |
| `POST /webhooks/mercadopago?data.id=&type=` | MP signature | MP body (ignored; headers + query used) | `200` always for handled/ignored/duplicate |
| `GET /me/orders` | bearer | — | `200 OrderSummary[]` `{id, status, totalAmount, currency, createdAt, itemCount}` |
| `GET /me/orders/{id}` | bearer, owner | — | `200 OrderDetail` `{..., items[{productName, variantName, unitPrice, quantity, keys?: string[]}]}` — `keys` present only when `Delivered` |
| `GET /admin/orders?status=AwaitingFulfillment` | bearer + Admin | — | `200 AdminOrder[]` `{id, buyerEmail, status, paidAt, items[{itemId, variantId, productName, variantName, quantity, assigned}]}` |
| `POST /admin/orders/{id}/items/{itemId}/keys` | bearer + Admin | `{code}` | `200 {orderStatus, items[{itemId, quantity, assigned}]}` |
| `GET /health` / `GET /health/live` | public | — | `200` (DB check / process only) |

**Error mapping**

| Status | Source | Examples |
|---|---|---|
| 400 | malformed request only (JSON parse failure, wrong types, unparseable GUID in the body or route) | `quantity: "abc"`, invalid GUID, truncated JSON |
| 401 | JwtBearer challenge; webhook signature; `OptionalBearerFilter` | expired token, bad `x-signature` |
| 403 | Admin policy | valid token, `sub` not in allowlist (or allowlist empty) |
| 404 | `NotFoundException` | unknown slug, order not owned by caller (ownership failures are 404, not 403, to avoid confirming existence) |
| 409 | `DomainConflictException`; `DbUpdateConcurrencyException` after the single retry (rethrown as `DomainConflictException`) | attach key to a `Delivered`/`Paid` order; **item already has `Quantity` keys**; the losing side of two concurrent attaches when the retry also conflicts |
| 422 | `DomainException` (non-conflict) | **missing or invalid buyer email**, empty items, `quantity` outside 1..10, more than 20 items, inactive/unknown variant, key variant does not match the item, empty `code` |
| 503 | `PaymentGatewayException`; `Payments:WebhookEnabled=false` | MP API down; kill switch |
| 500 | anything else | logged with correlation id; body has no exception detail |

## 8. Key encryption

- `KeyCipher` (Application, concrete): `Encrypt(string plaintext) → (byte[] blob, short version)` and `Decrypt(byte[] blob, short version) → string`, using `AesGcm` with a random 12-byte nonce per call, 16-byte tag.
- Key material: `Keys:EncryptionKey` (env `Keys__EncryptionKey`), base64 of 32 bytes; validated at startup (fail fast). `Keys:CurrentVersion = 1`. A future rotation adds `Keys:EncryptionKeys:{version}` and re-encrypts in a background change; no schema change.
- Plaintext appears in exactly two places: `GetMyOrder` (when `Delivered`, for the owner) and `OrderDeliveredHandler` (email body). `AttachKeyToOrderItem` receives plaintext in the request and encrypts immediately.
- Never logged: `SensitiveDataPolicy` (Serilog `IDestructuringPolicy`) reduces `Key`, `AttachKeyRequest`, and `OrderDetail` to id/status fields; request-body logging is disabled; the outbox payload carries ids only.

## 9. Frontend architecture (Nuxt 3)

**Repository layout.** The Nuxt app lives at the root of `maxkeys-front` (tree in section 3): `nuxt.config.ts`, `app.vue`, `components/`, `composables/`, `pages/`, `middleware/`, `types/`, `tests/` are top-level. Vercel builds from the repo root with no root-directory override.

**Contract source.** The frontend repo carries no `openspec/`. Its README links to `maxkeys-back/openspec/changes/mvp-marketplace/` and `types/api.ts` mirrors the DTOs of design section 7 by hand. Section 7 is the single source of truth; every frontend PR that touches `types/api.ts` cites the section 7 row it mirrors, and every backend PR that changes a DTO lists the affected `types/api.ts` type in its description so the follow-up frontend PR is explicit (ADR-18).

**Backend URL.** `runtimeConfig.public.apiBaseUrl` is fed by `NUXT_PUBLIC_API_BASE_URL` and points at the backend deploy URL (Fly/Railway) in production, `http://localhost:8080` in development.

| Piece | Design |
|---|---|
| **Tokens → Tailwind** | `theme.extend`: `colors.bg = #0A0A0E`, `colors.surface = #12121A`, `colors.accent = { DEFAULT: #7C5CFC, hover: #8F6FFF }`, `colors.success = #22D3A8`, `fontFamily.display = ['Space Grotesk']`, `fontFamily.sans = ['Inter']`, `backdropBlur.glass = 12px`, `borderRadius.xl`. Mock CSS variables become Tailwind utilities; no runtime CSS vars except fonts |
| **`useCart`** | `useState<CartState>('cart', () => ({ lines: [] }))`; `CartLine = { variantId, productSlug, productName, variantName, unitPrice, currency, quantity, imageUrl }`; computed `count`, `subtotal`; actions `add`, `remove`, `setQuantity` (1..10), `clear`. Persisted to `localStorage['nexo.cart.v1']` in a client-only `watch`; hydrated in `onMounted` to avoid SSR mismatch. Prices are indicative; the server recomputes |
| **`useCheckout`** | `status: idle \| submitting \| redirecting \| error`; `submit(email)` → `POST /checkout/orders` with `lines.map(({variantId, quantity}))` → `sessionStorage['nexo.lastOrderId']` → `window.location.href = initPoint`. `result.vue` polls `GET /checkout/orders/{id}/status` (3s, 20 tries) and clears the cart only when `status !== 'Pending'` or MP query `status=approved` |
| **`useAuth`** | Wraps `useSupabaseClient()` / `useSupabaseUser()`: `user`, `isLoggedIn`, `signInWithGoogle()` (`redirectTo = ${siteUrl}/auth/callback`), `signOut()`. Email prefill on checkout from `user.email`, editable; `UserId` still linked server-side from the bearer |
| **`middleware/auth.ts`** | If no `useSupabaseUser()`, save target path in cookie `nexo.redirect` and `navigateTo('/?login=1')` (opens `LoginDialog`). `auth/callback.vue` waits for the user, reads the cookie, navigates. Module `redirect: false` because most pages are public |
| **`useApi`** | `$fetch.create({ baseURL: runtimeConfig.public.apiBaseUrl, onRequest: attach Bearer from useSupabaseSession().access_token when present, onResponseError: throw ApiError(problemDetails) })`. Data fetching via `useAsyncData` for SSR-friendly catalog pages |
| **States** | Every page renders one of `Skeleton` (pending), `ErrorState` (ApiError with retry), `EmptyState` (no data), content. `OrderStatusBadge` maps the five statuses to token colors; `KeyReveal` renders masked keys with reveal/copy, only when `Delivered` |
| **Mis compras** | `account/orders/*` pages use `middleware: 'auth'`; detail page shows items and, for `Delivered`, a `KeyReveal` per item |

## 10. Configuration matrix

Secrets marked (S). Local development uses `dotnet user-secrets` for (S) values; production uses platform env vars (`__` separator).

| Key (appsettings / env) | Dev | Prod | Notes |
|---|---|---|---|
| `ConnectionStrings:Default` (S) | local Postgres or Supabase dev project | Supabase pooled connection string | `Include Error Detail=false` in prod |
| `Auth:Mode` | `Jwks` | `Jwks` (fallback `Hs256`) | Open item: confirm Supabase signing mode |
| `Auth:Issuer` | `https://<ref>.supabase.co/auth/v1` | same | validated |
| `Auth:Audience` | `authenticated` | same | |
| `Auth:JwksUrl` | `https://<ref>.supabase.co/auth/v1/.well-known/jwks.json` | same | used when `Mode=Jwks` |
| `Auth:Hs256Secret` (S) | optional | set only if `Mode=Hs256` | Supabase legacy JWT secret, UTF-8 bytes |
| `Auth:AdminSubs` | `["<your sub>"]` | operator sub(s) | empty → startup Warning, all admin routes 403 |
| `Payments:AccessToken` (S) | MP test credentials | MP production | |
| `Payments:WebhookSecret` (S) | from MP webhook config | same | |
| `Payments:WebhookEnabled` | `true` | `true`; set `false` to pause (503) | kill switch |
| `Payments:NotificationUrl` | tunnel URL `/webhooks/mercadopago` | `https://api.<domain>/webhooks/mercadopago` | |
| `Payments:PublicKey` | — | — | not required for redirect-based Checkout Pro; reserved, unused |
| `Frontend:BaseUrl` | `http://localhost:3000` | Vercel production URL | MP `back_urls` target (`/checkout/result`) |
| `Cors:AllowedOrigins` (array) | `["http://localhost:3000"]` | Vercel production origin + preview origins as needed | Backend CORS allowlist; the frontend is a separate origin because it deploys from `maxkeys-front` (ADR-18). No credentials mode (bearer header) |
| `Keys:EncryptionKey` (S) | random 32 bytes base64 | separate key | validated at startup |
| `Keys:CurrentVersion` | `1` | `1` | |
| `Storage:R2PublicBaseUrl` | `https://<bucket>.r2.dev` or custom domain | custom domain | URL builder only (ADR-12) |
| `Email:Sender` | `Logging` | `Smtp` | picks `LoggingEmailSender` / `SmtpEmailSender` |
| `Email:From`, `Email:OperatorTo` | any | real | Reconciled in PR12 task 12.1 to match the property name already shipped in `EmailOptions` since PR7a (`Email:OperatorAddress` never existed in code) |
| `Email:Smtp:Host/Port/UseStartTls/User/Password(S)` | — | provider values | generic SMTP (ADR-08) |
| `Outbox:PollIntervalSeconds/BatchSize/LeaseSeconds/MaxAttempts` | `5/10/300/8` | same | |
| `Serilog:MinimumLevel` | `Debug` | `Information` | |
| **`maxkeys-front`** `NUXT_PUBLIC_API_BASE_URL` | `http://localhost:8080` | backend deploy URL (Fly/Railway) | Vercel project env; feeds `runtimeConfig.public.apiBaseUrl` |
| **`maxkeys-front`** `SUPABASE_URL`, `SUPABASE_KEY` | dev project anon key | prod anon key | Vercel project env; consumed by `@nuxtjs/supabase` |
| **`maxkeys-front`** `NUXT_PUBLIC_SITE_URL` | `http://localhost:3000` | Vercel production URL | OAuth `redirectTo` |

All rows above the bold `maxkeys-front` rows belong to the backend repository (`appsettings*.json`, user-secrets, Fly/Railway env). The frontend rows live only in `maxkeys-front/.env.example` and the Vercel project settings.

## 11. Testing strategy

| Project | What | Approach |
|---|---|---|
| `Maxkeys.Domain.Tests` | every transition in 4.2 (valid + invalid), `Order.Create` pricing/snapshots, `AttachKey` all-or-nothing (2 of 3 keys stays `AwaitingFulfillment`; 3rd flips `Delivered` once), variant mismatch, `Key.AssignTo`, `OutboxEvent` backoff math (`30s * 2^n`, `Failed` at 8) | pure xUnit, `FakeTimeProvider`, no I/O |
| `Maxkeys.Application.Tests` | `ProcessPaymentNotification`: same request id x3 → one `Paid`, one outbox row; different request ids for same payment → state guard no-op; amount mismatch → ignored; `rejected` on `Pending` → `LastPaymentAttempt*` recorded, still `Pending`, zero outbox rows, then a later `approved` still transitions; `refunded` → log-only, no fields written; `CreateOrder` recompute vs tampered prices, missing email → 422; `AttachKeyToOrderItem`: over-quantity attach → 409, concurrency (two parallel attaches → one 409 then success on retry); outbox claim (two concurrent claimers never claim the same row; expired lease is reclaimable); `KeyCipher` round-trip + column is not plaintext; `OrderDeliveredHandler` email contains all keys once | xUnit + **Testcontainers Postgres** (ADR-09); real `AppDbContext`; `FakePaymentGateway`, `RecordingEmailSender` |
| `Maxkeys.Api.Tests` | signature validator vectors (valid, tampered `v1`, missing header → 401); `WebhookEnabled=false` → 503; JWT: `Hs256` mode with a locally signed token → 200; JWKS mode with a test key pair served from an in-process endpoint → 200, wrong `aud` → 401; `/admin/*` non-allowlisted → 403, empty allowlist → 403; `/me/orders/{id}` other owner → 404; Problem Details shape for each status | `WebApplicationFactory<Program>` + the same Postgres fixture |
| `maxkeys-front/tests` (Vitest) | `useCart` add/merge/setQuantity clamps/persistence; `useCheckout` request body shape and state machine; `VariantSelector` emits the selected variant | Vitest + `@nuxt/test-utils`, `$fetch` mocked |

Not automated in MVP: MP sandbox end-to-end (manual runbook in the deploy slice), SMTP delivery (LoggingEmailSender in tests).

## 12. Observability and deployment

**Observability**

| Concern | Decision |
|---|---|
| Sinks | Console: compact rendered text in Development, JSON (`CompactJsonFormatter`) in Production (Fly/Railway aggregate stdout) |
| Enrichers | `FromLogContext`, `WithMachineName`, correlation id from `CorrelationIdMiddleware` (`X-Correlation-Id` in → echoed out; generated otherwise); `UseSerilogRequestLogging` with path/status/elapsed |
| Domain context | `LogContext.PushProperty("OrderId"/"PaymentId"/"OutboxEventId")` inside use cases |
| Masking | `SensitiveDataPolicy` (section 8); `Auth:Hs256Secret`, tokens, `Payments:*Secret` never logged; MP request/response bodies logged at `Debug` only, with `access_token` redacted |
| Health | `/health` = Npgsql check; `/health/live` = process only. Fly/Railway health checks use `/health/live` |

**Deployment — two repositories, two independent pipelines (ADR-18)**

| Concern | Repository | Decision |
|---|---|---|
| Backend build | `maxkeys-back` | Fly.io/Railway build from the repo root `Dockerfile`. Multi-stage: `mcr.microsoft.com/dotnet/sdk:8.0` restore/publish (`-c Release`) → `mcr.microsoft.com/dotnet/aspnet:8.0`, non-root user, `ASPNETCORE_URLS=http://+:8080`, `EXPOSE 8080` |
| Migrations | `maxkeys-back` only | `Maxkeys.Api --migrate` runs `Database.Migrate()` and exits. Fly: `release_command`; Railway: pre-deploy command. Web instances never migrate on start (multi-instance race). The frontend pipeline never touches the database |
| Backend env | `maxkeys-back` | `deploy/fly.toml` / `deploy/railway.json` with the section-10 backend keys; secrets via `fly secrets set` / Railway variables |
| Frontend build | `maxkeys-front` | Vercel Git integration on the repo root (no root-directory override), Nuxt Nitro preset (SSR). Env: `NUXT_PUBLIC_API_BASE_URL`, `SUPABASE_URL`, `SUPABASE_KEY`, `NUXT_PUBLIC_SITE_URL`. Preview deployments get their own origin; add them to `Cors:AllowedOrigins` only when needed |
| CORS | `maxkeys-back` | `Cors:AllowedOrigins` (Vercel production origin, optional preview origins, `http://localhost:3000` in dev); no credentials mode (bearer header) |
| Release coordination | both | Backend deploys first when a DTO changes (additive fields are backward compatible); the frontend PR mirroring the change follows. No cross-repo PR exists |
| Rollback | previous image redeploy, `dotnet ef database update <prev>` (additive schema), Vercel instant rollback, `Payments:WebhookEnabled=false` (503 → MP retries), `Auth:Mode` switch; sandbox and production use separate `Payments:AccessToken` / `Payments:WebhookSecret` values so a rollback never mixes environments |

## 13. Natural PR slice boundaries (input for sdd-tasks)

Estimates are authored lines (additions + deletions); generated EF migration code is excluded from the risk count. Each slice builds and tests green on its own. **Two chains, one per repository (ADR-18):** PR1–PR12 and PR18a stack inside `maxkeys-back`; PR13–PR17 and PR18b stack inside `maxkeys-front` (PR13's base is that repo's own initial commit on `main`, created in the same slice). No PR spans both repositories; cross-repo dependencies (e.g. PR16 needs the PR9 endpoints deployed or run locally) are stated in the PR description, not enforced by Git.

| # | Repo | Slice | Depends on | Est. lines |
|---|---|---|---|---|
| 1 | maxkeys-back | Solution skeleton, `Directory.Build.props`, Domain: `Common`, `Catalog`, `Outbox`, `Payments` entities | — | ~250 |
| 2 | maxkeys-back | Domain: `Order`, `OrderItem`, `Key`, transitions, `AttachKey`; Domain.Tests | 1 | ~380 |
| 3 | maxkeys-back | Infrastructure persistence: `AppDbContext`, configurations, initial migration; Testcontainers fixture | 2 | ~350 (+ generated migration) |
| 4 | maxkeys-back | `KeyCipher` + options + tests | 1 | ~150 |
| 5 | maxkeys-back | Application: catalog queries, `CreateOrder`, `GetOrderStatus`, `IPaymentGateway` + `FakePaymentGateway` (test), tests | 3 | ~350 |
| 6 | maxkeys-back | Application: `ProcessPaymentNotification` (approved / attempt-recorded / ignored branches) + idempotency/replay tests | 5 | ~320 |
| 7 | maxkeys-back | Outbox (Application + Infrastructure): `IOutboxHandler`, `OrderApprovedHandler`, **`IEmailSender` + `EmailMessage` + `LoggingEmailSender` (Infrastructure) + `RecordingEmailSender` (test fake)**, operator email template, `OutboxProcessor`, claim query, tests | 3 | ~380 |
| 8 | maxkeys-back | Application: `AttachKeyToOrderItem`, `ListOrdersAwaitingFulfillment`, `OrderDeliveredHandler` + buyer email template, `GetMyOrders/GetMyOrder`, tests | 4, 7 | ~380 |
| 9 | maxkeys-back | Api skeleton: `Program`, Problem Details handler, Serilog + correlation, health, CORS (`Cors:AllowedOrigins`), catalog + checkout endpoints, Dockerfile. **Builds and runs green without MP credentials**: `NotConfiguredPaymentGateway` (Infrastructure) is registered as `IPaymentGateway` when `Payments:AccessToken` is empty and turns `POST /checkout/orders` into a 503 Problem Details; `Email:Sender=Logging` is the default. Slice 11 adds the real gateway and keeps the stub for credential-less dev | 5, 7 | ~370 |
| 10 | maxkeys-back | Api auth: `JwtSetup` (JWKS/HS256), `JwksKeyCache`, Admin policy, `OptionalBearerFilter`, `/me` + `/admin` endpoints, Api.Tests | 8, 9 | ~380 |
| 11 | maxkeys-back | MP integration: `MercadoPagoGateway`, `SignatureValidator`, webhook endpoint, kill switch, gateway selection by config, tests | 6, 9 | ~350 |
| 12 | maxkeys-back | Email + seed: `SmtpEmailSender` (MailKit), `EmailOptions`, sender selection by `Email:Sender`, `CatalogSeeder` + `seed/catalog.json` + `--seed-catalog` flag | 7, 9 | ~260 |
| 13 | maxkeys-front | Repo bootstrap (`git init -b main`, initial commit with README linking the backend `openspec/` contract, `.env.example`) + Nuxt scaffold: `nuxt.config.ts`, Tailwind tokens, `useApi`, `types/api.ts` (catalog + checkout DTOs from section 7), layout, ui primitives | — (backend PR9 for a live API; mocks otherwise) | ~380 |
| 14 | maxkeys-front | Catalog + product pages + `VariantSelector` + tests | 13 | ~350 |
| 15 | maxkeys-front | Cart: `useCart`, `CartDrawer`, `CartLine`, tests | 13 | ~300 |
| 16 | maxkeys-front | Checkout + result page, `useCheckout`, tests | 15 (backend PR9/PR11 for end-to-end) | ~300 |
| 17 | maxkeys-front | Auth: `useAuth`, `LoginDialog`, middleware, callback, Mis compras + `KeyReveal`, `types/api.ts` order DTOs | 14 (backend PR10 for end-to-end) | ~380 |
| 18a | maxkeys-back | Deploy config: `deploy/fly.toml`, `deploy/railway.json`, `--migrate` flag wiring, `Cors:AllowedOrigins` prod values, sandbox runbook (`docs/runbook-sandbox.md`) | 11, 12 | ~180 |
| 18b | maxkeys-front | Vercel config (`vercel.json` if needed, Nitro preset), `.env.example` finalization, README env docs pointing `NUXT_PUBLIC_API_BASE_URL` at the backend deploy URL | 17 | ~80 |

## 14. Architecture Decision Records

| ADR | Decision | Rejected alternatives | Rationale |
|---|---|---|---|
| **ADR-01** Layering | Four projects; Application owns `IAppDbContext` (with `DbSet<T>`) and references EF Core abstractions | Repositories per entity; Application free of EF types | `IAppDbContext` exists for the compile-time layering rule (Application must not reference Infrastructure), not for mocking; tests use the real context on Postgres. Repositories would be a second abstraction over `DbSet` with one implementation |
| **ADR-02** Use cases as plain classes | One class per use case with `ExecuteAsync`, registered in DI, injected into endpoints | MediatR; generic `IUseCase<TIn,TOut>` | Readability rule; no pipeline behaviors are needed yet, and a `Func` call is easier to trace than a mediator |
| **ADR-03** Fourth interface: `IOutboxHandler` (user-accepted deviation, 2026-09-12) | `IOutboxHandler { string EventType; HandleAsync }` with `OrderApprovedHandler`, `OrderDeliveredHandler` | `switch` on `Type` inside the processor | Two real implementations exist, satisfying the "no interface without a second implementation" rule; the processor stays ignorant of business handlers |
| **ADR-04** Buyer delivery email via outbox `OrderDelivered` (user-accepted deviation, 2026-09-12) | Insert `OrderDelivered` in the same tx as `→ Delivered`; handler sends the email. Guarantee: one send per `Delivered` transition; handler retries may duplicate if the send succeeds but marking fails (at-least-once) | Send inline after `SaveChanges` in `AttachKeyToOrderItem` | Inline is a dual write: a transient SMTP failure after the commit loses the email with no retry and no resend endpoint in scope. The outbox already exists; retries and dead-letter come for free. sdd-tasks must carry the `One-Time Delivery Email` spec-wording clarification |
| **ADR-05** `Order.UpdatedAt` bump on `AttachKey` (user-accepted deviation, 2026-09-12) | Always touch the order row so `xmin` changes | Rely on `Status` changes only; serializable transactions | Guarantees a conflict for concurrent attaches; one retry, then 409. Serializable isolation is heavier and less explicit |
| **ADR-06** Outbox lease via `next_attempt_at` | Claim sets `Processing` + `next_attempt_at = now + lease`; claim query includes expired `Processing` rows | Separate `claimed_at`/`claimed_by` columns; never reclaim `Processing` | Crash recovery without a new column; the same index serves both cases |
| **ADR-07** Enums as strings | `text` columns with max length | `int` columns | Operator will read orders/outbox in psql during manual fulfillment; adding a value needs no migration |
| **ADR-08** Email = generic SMTP via MailKit | `Email:Smtp:*` settings; `LoggingEmailSender` in dev | Provider SDK (Resend/SendGrid); Nuxt/Vercel-side email | Resend, SendGrid, Postmark, Gmail and Brevo all expose SMTP, so the provider is a config change, not code; MailKit is the maintained .NET client (`System.Net.Mail.SmtpClient` is deprecated). Provider SDK would tie `IEmailSender` to one vendor before one is chosen |
| **ADR-09** Tests on Testcontainers Postgres | Application and Api tests use a real Postgres container; env override `TEST_POSTGRES_CONNECTION` when Docker is unavailable | EF InMemory; SQLite in-memory | `FOR UPDATE SKIP LOCKED`, `xmin`, `jsonb`, partial unique indexes and PK-violation dedupe do not exist in InMemory/SQLite; testing there would validate a different system. Cost: Docker locally and in CI (GitHub Actions has it) |
| **ADR-10** MP via typed `HttpClient`, not the SDK | Two calls (`POST /checkout/preferences`, `GET /v1/payments/{id}`) behind `IPaymentGateway` | Official `mercadopago` NuGet SDK | Two endpoints do not justify the SDK's surface and its own HTTP stack; the seam keeps swapping possible |
| **ADR-11** Order status polling endpoint is public by GUID | `GET /checkout/orders/{id}/status` returns status + masked email only | Require auth (breaks guests); signed token in back_url | Guests need a result page; a v4 GUID is unguessable and the response carries no keys or PII beyond a masked email |
| **ADR-12** Catalog managed by seed file; R2 as public URL builder only (user-approved deviation from the "fixed stack" line, 2026-09-12) | `seed/catalog.json` upserted by `Maxkeys.Api --seed-catalog <path>`; images uploaded via the Cloudflare dashboard; API composes `{Storage:R2PublicBaseUrl}/{ImageKey}`. **No S3 SDK, no S3 credentials, no upload endpoint in MVP** | Admin catalog endpoints; S3-compatible SDK for uploads (as written in `openspec/config.yaml` context) | No catalog UI is in scope; a JSON file reviewed in PRs is the simplest auditable source. The S3-compatible SDK enters when an upload endpoint exists. Deviation from the config's "Cloudflare R2 (S3-compatible SDK)" line explicitly approved by the user on 2026-09-12 |
| **ADR-13** Migrations by `--migrate` flag at release | Same binary, `Database.Migrate()` then exit, run as Fly `release_command` / Railway pre-deploy | Auto-migrate on web startup; EF migration bundle | Startup migration races across instances; bundle is a second artifact. One binary, one command |
| **ADR-14** Ownership failures return 404 | Non-owner on `/me/orders/{id}` → 404 | 403 | Avoids confirming that an order id exists |
| **ADR-15** Non-approved MP statuses never change `Status`; `rejected`/`pending`/`in_process` are recorded as the last payment attempt | Only `approved` transitions. `rejected`, `pending`, `in_process` on a `Pending` order call `Order.RecordPaymentAttempt` (writes `LastPaymentAttemptId/Status/At`, no outbox row). All other statuses (`refunded`, `charged_back`, `cancelled`, `authorized`, unknown) hit the generic log-only branch | `rejected → Cancelled`; treating every non-approved status as a silent ignore | Checkout Pro lets the buyer retry the same preference after a rejection, so cancelling would strand a payable order. Recording the attempt keeps the order observable (operator support, result page) without inventing a state. Refunds/chargebacks stay fully out of scope, hence log-only |
| **ADR-16** Snake_case naming convention | `EFCore.NamingConventions` | EF defaults (PascalCase quoted identifiers) | Raw claim SQL and psql operations read naturally |
| **ADR-17** `TimeProvider` for time | Inject BCL `TimeProvider`; `FakeTimeProvider` in tests | Custom `IClock` | BCL type, no new interface |
| **ADR-18** Two repositories: `maxkeys-back` (`AlanMartinez/maxkey-back`) and `maxkeys-front` (`AlanMartinez/maxkey-front`) | Backend repo owns `openspec/`, solution, tests, seed, Dockerfile, deploy config; frontend repo is the Nuxt app at its root with README linking the backend `openspec/` as contract source; `types/api.ts` mirrors section 7 by hand | Monorepo with `frontend/` subfolder (as originally designed in this document and the proposal) | User decision 2026-09-12. Independent deploy pipelines: Fly/Railway and Vercel each build from a repo root with zero path configuration; frontend PRs never trigger backend CI and vice versa; the two PR chains stay small and reviewable in isolation. **Consequences accepted:** (1) DTO drift risk between section 7 and `types/api.ts` — mitigated by keeping section 7 the single source, citing it in frontend PRs, and listing affected types in backend PRs; (2) CORS is now mandatory (`Cors:AllowedOrigins`), not a same-origin convenience; (3) SDD artifacts (specs, design, tasks) live only in the backend repo, so frontend work references them by link; (4) no atomic cross-repo change — backend deploys first with additive DTO changes |

## 15. Threat Matrix

N/A — this change adds no routing, shell command, subprocess, VCS/PR automation, executable-file classification, or process-integration boundary in the sense of `references/threat-matrix.md`. External-input boundaries (webhook, JWT, admin key upload) are covered by the controls in sections 6(b), 6(e), 7 and 8 and by the tests in section 11.

## 16. Migration / Rollout

No data migration (greenfield). Rollout follows the slice order in section 13 under auto-chain, as two independent chains (backend in `maxkeys-back`, frontend in `maxkeys-front`); the auth slice (10) is blocked on the Supabase signing-mode confirmation; the MP slice (11) requires sandbox credentials and a public tunnel for the manual end-to-end run in slice 18a. The frontend chain can start in parallel with the backend chain and run against mocked responses until PR9 is deployable.

## 17. Risks and open items

| Risk / open item | Status | Mitigation |
|---|---|---|
| Supabase signing mode (JWKS vs HS256) unconfirmed | **Open — blocks slice 10** | `Auth:Mode` switch; confirm in Supabase dashboard (Auth → JWT keys) before apply |
| MP signature manifest details (segment order, lowercase `data.id`) | Medium | Validate against MP docs during slice 11; unit vectors from a real sandbox notification |
| Testcontainers requires Docker locally | Medium | `TEST_POSTGRES_CONNECTION` override to a Supabase dev database |
| Duplicate operator/buyer email on retry after send-but-mark-failed | Low | Accepted at-least-once; noted in ADR-04. **sdd-tasks: carry the `One-Time Delivery Email` spec-wording clarification** |
| Caps `Order <= 20 items` and `OrderItem.Quantity 1..10` are design-introduced constraints not present in any spec | **Open — spec drift** | Kept (they bound the MP preference size and the admin attach loop). **sdd-tasks must add both caps to the cart-checkout spec** as 422 scenarios |
| `LastPaymentAttempt*` fields and `lastPaymentAttemptStatus` in the status response are additive to the proposal's data model | Low | Required by the payments-webhook `Rejected Payment Handling` requirement; three nullable columns in the initial migration |
| **Frontend/backend contract drift** (`maxkeys-front/types/api.ts` vs backend DTOs) now that the repos are separate | Medium | Section 7 is the single source of truth; frontend PRs cite the section 7 row they mirror; backend PRs that change a DTO list the affected `types/api.ts` types; DTO changes are additive (new optional fields) so the older frontend keeps working; backend deploys first. Rollback: revert the frontend PR independently (Vercel instant rollback) — the backend never depends on frontend types (ADR-18) |
| CORS misconfiguration blocks the frontend after a Vercel origin change (new custom domain, preview URL) | Low | `Cors:AllowedOrigins` is config, not code; startup log lists the allowed origins; `/health` stays reachable for diagnosis |
| `Failed` outbox events need manual attention | Low | `Error` log with event id; SQL query; resurrection endpoint is a later change |
| `Pending` orders accumulate | Accepted | Later cleanup change (`Cancel()` exists) |
| Email provider account not yet chosen | Low | Generic SMTP (ADR-08); `LoggingEmailSender` until then |
| Catalog seeding ergonomics | Low | JSON seed file; upgrade to endpoints when an admin UI arrives |
