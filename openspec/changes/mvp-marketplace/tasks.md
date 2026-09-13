# Tasks: MVP Marketplace (Nexo)

Source of truth for scope/design: `proposal.md`, `specs/{domain}/spec.md`, `design.md` (section 3 = repositories, section 13 = PR slices, section 14 = ADRs). Two repositories (ADR-18) — no source code existed until PR0.

## Review Workload Forecast

| Field | Value |
|---|---|
| Estimated changed lines | ~5,930 authored lines across 20 PRs (generated EF migration code excluded per design section 13) |
| 400-line budget risk | Medium — no PR exceeds 400, but PR2, PR7b, PR8, PR10, PR13, PR17 sit at ~380 (20-line margin); PR7 itself split into PR7a (361 actual)/PR7b (~380) |
| Chained PRs recommended | Yes |
| Suggested split | PR0 (`maxkeys-back` bootstrap) → PR1‑PR12, PR18a (backend stack, `maxkeys-back`) ‖ PR13‑PR17, PR18b (frontend stack, `maxkeys-front`) |
| Delivery strategy | auto-chain |
| Chain strategy | stacked-to-main |

```text
Decision needed before apply: No
Chained PRs recommended: Yes
Chain strategy: stacked-to-main
400-line budget risk: Medium
```

`auto-chain` + cached `stacked-to-main` ⇒ orchestrator proceeds directly to PR0 with `sdd-apply`; no user decision blocks the start.

### Repositories (ADR-18 — two repos, two independent `sdd-apply` working directories)

| Repo | GitHub | Local folder | PRs | Owns |
|---|---|---|---|---|
| `maxkeys-back` | `AlanMartinez/maxkey-back` | `C:\Personal\Projects\maxkeys-back` (renamed from `maxkeys` after this session) | PR0, PR1–PR12, PR18a | `openspec/` (single source of truth for both repos), `Maxkeys.sln`, `src/`, `tests/`, `seed/`, `Dockerfile`, `deploy/` |
| `maxkeys-front` | `AlanMartinez/maxkey-front` | `C:\Personal\Projects\maxkeys-front` (sibling folder) | PR13–PR17, PR18b | Nuxt 3 app at the repo root (no `frontend/` prefix); no `openspec/` copy — README links to `maxkeys-back/openspec/changes/mvp-marketplace/` as the contract source |

`sdd-apply` must `cd` into the matching local folder before starting a PR in that repo's chain. No PR spans both repositories.

### PR Chain Order (authoritative — use this table for branch/base wiring)

Two independent stacks, one per repository (ADR-18), joined only by the shared deploy milestone (PR18a + PR18b, each in its own repo — no PR spans both).

| PR | Repo | Title | Branch | Base branch | Depends on | Est. lines | Phase | Focused test | Runtime harness | Rollback boundary |
|---|---|---|---|---|---|---|---|---|---|---|
| 0 | maxkeys-back | Repo bootstrap | `chore/mvp-00-repo-bootstrap` | — (first commit) | — | ~40 | 0 | none (scaffolding only) | N/A — no runtime yet | delete `.git`, redo `git init` |
| 1a | maxkeys-back | Domain foundation (scaffold + catalog) | `feat/mvp-01-domain-foundation` | `main` | 0 | 324 (actual) | 1 | `dotnet test tests/Maxkeys.Domain.Tests --filter "Product"` | N/A — no host yet | delete `Maxkeys.Domain/{Common,Catalog}` + `Domain.Tests` |
| 1b | maxkeys-back | Domain outbox + webhook dedupe | `feat/mvp-01b-domain-outbox` | `feat/mvp-01-domain-foundation` → `main` after PR1a | 1a | 267 (actual) | 1 | `dotnet test tests/Maxkeys.Domain.Tests --filter "OutboxEvent|ProcessedWebhookNotification"` | N/A | delete `Maxkeys.Domain/{Outbox,Payments}` + their tests |
| 2a | maxkeys-back | Order core (status, item, aggregate) | `feat/mvp-02a-order-core` | `feat/mvp-01b-domain-outbox` → `main` after PR1b | 1b | 400 (actual) | 1 | `dotnet test tests/Maxkeys.Domain.Tests --filter Order` | N/A | delete `Orders/`; revert cart-checkout spec edit |
| 2b | maxkeys-back | Order keys + delivery derivation | `feat/mvp-02b-order-keys` | `feat/mvp-02a-order-core` → `main` after PR2a | 2a | ~200 | 1 | `dotnet test tests/Maxkeys.Domain.Tests --filter "Order\|Key"` | N/A | delete `Keys/`, `Order.AttachKey` |
| 3a | maxkeys-back | EF Core persistence: projects + AppDbContext + configurations | `feat/mvp-03-ef-persistence` | `feat/mvp-02b-order-keys` → `main` after PR2b | 2b | 362 (actual) | 2 | `dotnet build` | N/A — no migration/tests yet in this slice | delete `src/Maxkeys.Application`, `src/Maxkeys.Infrastructure` |
| 3b | maxkeys-back | EF Core InitialCreate migration + Testcontainers fixture | `feat/mvp-03b-ef-testcontainers` | `feat/mvp-03-ef-persistence` → `main` after PR3a | 3a | 238 (actual, +1,100-line generated migration excluded) | 2 | `dotnet test tests/Maxkeys.Application.Tests --filter AppDbContext` | `dotnet ef database update` against Testcontainers/`TEST_POSTGRES_CONNECTION` | `dotnet ef database update 0`; delete `Infrastructure/Persistence/Migrations`, `tests/Maxkeys.Application.Tests` |
| 4 | maxkeys-back | KeyCipher | `feat/mvp-04-key-cipher` | `feat/mvp-03b-ef-testcontainers` → `main` after PR3b | 3b | ~150 | 2 | `dotnet test tests/Maxkeys.Application.Tests --filter KeyCipher` | N/A — pure crypto | delete `Security/KeyCipher*` |
| 5a | maxkeys-back | Catalog listing application | `feat/mvp-05a-catalog-application` | `feat/mvp-04-key-cipher` → `main` after PR4 | 4 | 336 (actual) | 3 | `dotnet test tests/Maxkeys.Application.Tests --filter GetCatalogTests` | N/A (pure Application, Postgres-backed test) | delete `Catalog/{CatalogDtos,StorageOptions,GetCatalog}.cs`, `tests/.../Catalog/{CatalogTestData,GetCatalogTests}.cs` |
| 5a2 | maxkeys-back | Product detail query + payment gateway seam | `feat/mvp-05a2-product-detail-gateway` | `feat/mvp-05a-catalog-application` → `main` after PR5a | 5a | 268 (actual) | 3 | `dotnet test tests/Maxkeys.Application.Tests --filter "GetProductBySlugTests\|Payments"` | N/A (`FakePaymentGateway`) | delete `Catalog/GetProductBySlug.cs`, `Payments/*`, `tests/.../Fakes/FakePaymentGateway.cs`, `tests/.../Catalog/GetProductBySlugTests.cs` |
| 5b | maxkeys-back | Checkout application | `feat/mvp-05b-checkout-application` | `feat/mvp-05a2-product-detail-gateway` → `main` after PR5a2 | 5a2 | 361 (actual) | 3 | `dotnet test tests/Maxkeys.Application.Tests --filter Checkout` | N/A (`FakePaymentGateway`) | delete `Checkout/*` |
| 6 | maxkeys-back | Payment webhook application | `feat/mvp-06-payment-webhook-application` | `feat/mvp-05b-checkout-application` → `main` after PR5b | 5b | 378 (actual) | 3 | `dotnet test tests/Maxkeys.Application.Tests --filter Payments` | N/A (`FakePaymentGateway`) | delete `Payments/ProcessPaymentNotification.cs` |
| 7a | maxkeys-back | Outbox handler seam + email seam | `feat/mvp-07a-outbox-handler-email` | `feat/mvp-06-payment-webhook-application` → `feat/mvp-07b-outbox-processor` after PR7b bases on it | 3 | 361 (actual) | 5 | `dotnet test tests/Maxkeys.Application.Tests --filter Outbox` | N/A (pure Application/Infrastructure seam; `OutboxProcessor` itself is PR7b) | delete `Application/Outbox/*`, `Application/Notifications/*`, `Infrastructure/Email/LoggingEmailSender.cs`, `tests/.../Fakes/RecordingEmailSender.cs`, `tests/.../Outbox/OrderApprovedHandlerTests.cs` |
| 7b | maxkeys-back | Outbox processor (poller) | `feat/mvp-07b-outbox-processor` | `feat/mvp-07a-outbox-handler-email` → `main` after PR7a | 3, 7a | ~380 | 5 | `dotnet test tests/Maxkeys.Application.Tests --filter Outbox` | run `OutboxProcessor` one poll cycle against Testcontainers | delete `Infrastructure/Outbox/*`, `tests/.../Outbox/OutboxClaimQueryTests.cs` |
| 8 | maxkeys-back | Fulfillment application + orders history | `feat/mvp-08-fulfillment-application` | `feat/mvp-07b-outbox-processor` → `main` after PR7b | 4, 7b | ~380 | 3 | `dotnet test tests/Maxkeys.Application.Tests --filter "Fulfillment\|Orders"` | N/A (`RecordingEmailSender` fake) | delete `Fulfillment/*`, `OrderDeliveredHandler`, `GetMyOrder(s)`; revert fulfillment spec edit |
| 9 | maxkeys-back | API skeleton | `feat/mvp-09-api-skeleton` | `feat/mvp-08-fulfillment-application` → `main` after PR8 | 5, 7 | ~370 | 4 | `dotnet build && dotnet test tests/Maxkeys.Api.Tests --filter Catalog` | `dotnet run --project src/Maxkeys.Api` with empty `Payments:AccessToken`, hit `/health` | delete `src/Maxkeys.Api` (revert to PR8 state) |
| 10 | maxkeys-back | API auth | `feat/mvp-10-api-auth` | `feat/mvp-09-api-skeleton` → `main` after PR9 | 8, 9 | ~380 | 4 | `dotnet test tests/Maxkeys.Api.Tests --filter Auth` | `dotnet run` + call `/me/orders` with a real Supabase token | delete `Auth/*`, `Me/Admin` endpoints |
| 11 | maxkeys-back | Mercado Pago integration | `feat/mvp-11-mercadopago-integration` | `feat/mvp-10-api-auth` → `main` after PR10 | 6, 9 | ~350 | 5 | `dotnet test tests/Maxkeys.Api.Tests --filter Webhooks` | MP sandbox notification via public tunnel (task 11.6) | revert DI to `NotConfiguredPaymentGateway`; delete `MercadoPago*`, `WebhookEndpoints` |
| 12 | maxkeys-back | Email + catalog seed | `feat/mvp-12-email-seed` | `feat/mvp-11-mercadopago-integration` → `main` after PR11 | 7, 9 | ~260 | 5 | `dotnet test tests/Maxkeys.Application.Tests --filter Catalog` | `dotnet run -- --seed-catalog seed/catalog.json` against a dev DB | set `Email:Sender=Logging`; delete `SmtpEmailSender`, `CatalogSeeder` |
| 13 | maxkeys-front | Repo bootstrap + Nuxt scaffold | `feat/mvp-13-frontend-scaffold` | own initial commit → `main` | — (backend PR9 for a live API; mocks otherwise) | ~380 | 6 | `npm run test && nuxi typecheck` | `npm run dev`, load `/` shell | delete repo contents / stop before first push |
| 14 | maxkeys-front | Frontend catalog + product | `feat/mvp-14-frontend-catalog` | `feat/mvp-13-frontend-scaffold` → `main` after PR13 | 13 | ~350 | 6 | `npm run test -- VariantSelector` | `npm run dev`, browse catalog against a running/mocked API | delete catalog/product pages+components |
| 15 | maxkeys-front | Frontend cart | `feat/mvp-15-frontend-cart` | `feat/mvp-14-frontend-catalog` → `main` after PR14 | 13 | ~300 | 6 | `npm run test -- useCart` | `npm run dev`, add/remove items, refresh (persistence) | delete `useCart.ts`, cart components |
| 16 | maxkeys-front | Frontend checkout + result | `feat/mvp-16-frontend-checkout` | `feat/mvp-15-frontend-cart` → `main` after PR15 | 15 (backend PR9/PR11 for end-to-end) | ~300 | 6 | `npm run test -- useCheckout` | `npm run dev`, submit checkout against a running API, land on result page | delete `useCheckout.ts`, checkout pages/components |
| 17 | maxkeys-front | Frontend auth + orders history | `feat/mvp-17-frontend-auth-orders` | `feat/mvp-16-frontend-checkout` → `main` after PR16 | 14 (backend PR10 for end-to-end) | ~380 | 6 | `npm run test` (full suite) | `npm run dev`, Google login round-trip against dev Supabase project | delete `useAuth.ts`, `LoginDialog`, auth middleware/callback, orders pages |
| 18a | maxkeys-back | Deploy config + runbook | `feat/mvp-18a-deploy-runbook` | `main` (after PR11 and PR12 merged) | 11, 12 | ~180 | 7 | `dotnet test` | manual sandbox runbook (task 18a.5) — real MP sandbox + Supabase user, no automated harness | revert deploy configs; redeploy previous image |
| 18b | maxkeys-front | Vercel config + env docs | `feat/mvp-18b-deploy-config` | `main` (after PR17 merged) | 17 | ~80 | 7 | `npm run test` | `npm run build` + Vercel preview deploy | revert Vercel config; Vercel instant rollback |

**Frontend independence**: PR13‑PR17 have zero dependency on backend PR1‑PR12 (they build/test against mocked `$fetch` and the DTO contract in design section 7) and live in a separate repository — they can be developed/merged in parallel on `maxkeys-front`'s own `main`-based stack.

**PR9 credential-less green build**: `NotConfiguredPaymentGateway` (task 9.7) makes PR9 build and run green with no Mercado Pago credentials configured; PR11 swaps in the real gateway without breaking that path.

**No cross-repo Git dependency**: cross-repo needs (e.g. PR16/PR17 wanting a live backend for end-to-end checks) are stated as notes in the PR description only; Git branching never spans both repos (design §13).

---

## Phase 0: Repo Bootstrap

**PR0** — `chore/mvp-00-repo-bootstrap` — base: none (first commit) — depends on: — — ~40 lines

- [x] 0.1 Run `git init`, set default branch to `main`.
- [x] 0.2 Create `.gitignore` (`bin/`, `obj/`, `node_modules/`, `.env`, `appsettings.*.local.json`, `.vs/`, `dist/`, `.nuxt/`, `.output/`).
- [x] 0.3 Commit `openspec/` and `mock ui/` as the initial commit on `main` (`chore: initial commit (openspec artifacts + UI mock)`). Commit `282668c` on `main`.
- [x] 0.4 **User action (not sdd-apply)**: configure the git remote (`git remote add origin <url>`) and push `main`. — done: remote configured, `main` pushed to `maxkeys-back` (`AlanMartinez/maxkey-back`).

Note: PR0 has no dedicated branch/PR — the initial commit lands directly on `main` since there is nothing to branch from yet. `chore/mvp-00-repo-bootstrap` is a nominal PR-chain label only; no branch was created for it.

## Phase 1: Domain

**PR1** — `feat/mvp-01-domain-foundation` — base: `main` — depends on: PR0 — ~250 lines

- [x] 1.1 Create `Maxkeys.sln`, `Directory.Build.props` (`net8.0`, nullable, implicit usings, `TreatWarningsAsErrors`), `src/Maxkeys.Domain/Maxkeys.Domain.csproj` (BCL only).
- [x] 1.2 Add `src/Maxkeys.Domain/Common/Entity.cs` (client-generated `Guid Id`), `DomainException.cs`, `DomainConflictException.cs : DomainException` (422 vs 409 split, design §7).
- [x] 1.3 Add `src/Maxkeys.Domain/Catalog/Product.cs` (`Slug` non-empty/lowercase/unique invariant, `Name`/`Platform` non-empty), `ProductVariant.cs` (`Price > 0`, `OldPrice` null or `> Price`, `Currency == "ARS"`).
- [x] 1.4 Add `src/Maxkeys.Domain/Outbox/OutboxEvent.cs` (`Claim(leaseUntil)`, `MarkProcessed(now)`, `MarkFailedAttempt(error, now, maxAttempts)`), `OutboxEventStatus.cs`, `OutboxEventTypes.cs` (`OrderApproved`, `OrderDelivered`).
- [x] 1.5 Add `src/Maxkeys.Domain/Payments/ProcessedWebhookNotification.cs` (`RequestId` non-empty, insert-only).
- [x] 1.6 Create `tests/Maxkeys.Domain.Tests` (xUnit): `ProductTests`, `ProductVariantTests`, `OutboxEventTests` — backoff math `30s * 2^n` and `Failed` at the 8th attempt (outbox-processing spec: `Exponential Backoff on Failure`, `Dead-Letter After Max Attempts`), using `FakeTimeProvider`. ****PR1 split (auto-chain)**: implementation measured 591 lines against the 400-line cap, so the slice was split into PR1a `feat/mvp-01-domain-foundation` (tasks 1.1–1.3 + catalog tests, 324 lines, 12 tests) and PR1b `feat/mvp-01b-domain-outbox` (tasks 1.4–1.5 + outbox/webhook tests, 267 lines, 11 tests), stacked-to-main. PR2 now bases on PR1b. Both branches build and test green (23/23 total).cs` remain). Driven by 6 non-trivial entity/exception files plus full spec-mandated test coverage (backoff exponential math, dead-letter, claim/process transitions) bundled in the same PR per this task list. Flagged to the orchestrator/user for a `size:exception` decision or a future retroactive split; PR2 was not started.

**PR2a** — `feat/mvp-02a-order-core` — base: PR1b → `main` after merge — depends on: PR1b — 400 lines (actual)

- [x] 2.1 Add `src/Maxkeys.Domain/Orders/OrderStatus.cs` (`Pending, Paid, AwaitingFulfillment, Delivered, Cancelled`).
- [x] 2.2 Add `src/Maxkeys.Domain/Orders/OrderItem.cs`: `Quantity` invariant **1..10**, snapshot names non-empty. (PR2a: no `Keys`/`IsComplete` yet — deferred to PR2b once `Key` exists.)
- [x] 2.4 (PR2a part) Add `src/Maxkeys.Domain/Orders/Order.cs`: factory `Create(userId?, buyerEmail, lines[], now)` enforcing **1..20 items**, email format, computed `TotalAmount`; transitions `AttachPreference`, `RecordPaymentAttempt`, `MarkPaid`, `MarkAwaitingFulfillment`, `Cancel`. (PR2b: `AttachKey` all-or-nothing → `Delivered`, bumps `UpdatedAt` per ADR-05.)
- [x] 2.5 **Spec carry-over** — update `specs/cart-checkout/spec.md`: add scenario "Order exceeds 20 items rejected" (422) under `Order Creation From Cart`, and scenario "Quantity outside 1..10 rejected" (422) matching the `Order.Create`/`OrderItem` invariants above.
- [x] 2.6 (PR2a part) `tests/Maxkeys.Domain.Tests/Orders/OrderTests.cs` + `OrderItemTests.cs`: creation happy path (total computed, snapshots kept); 21-item and 0-item orders → `DomainException`; invalid email → `DomainException`; quantity 0 and 11 → `DomainException`; every PR2a transition valid path + one invalid-state path → `DomainConflictException`; `RecordPaymentAttempt` sets the three `LastPaymentAttempt*` fields and keeps `Pending`; `MarkPaid` sets `MpPaymentId`/`PaidAt`/`LastPaymentAttemptStatus=approved`; `UpdatedAt` bump verified on a transition. (PR2b: `AttachKey` all-or-nothing, variant-mismatch, wrong-state-attach cases.)
- Test: `dotnet test tests/Maxkeys.Domain.Tests --filter Order` green (43/43 full suite green).

**PR2b** — `feat/mvp-02b-order-keys` — base: PR2a → `main` after merge — depends on: PR2a — 265 lines (actual, well under the 400 cap)

- [x] 2.3 Add `src/Maxkeys.Domain/Keys/Key.cs` (`EncryptedCode` non-empty, `KeyVersion >= 1`, `LoadedBy` non-empty), `KeyStatus.cs` (`Available, Assigned`), `AssignTo(orderItemId, now)`.
- [x] 2.4 (remainder) Add `OrderItem.Keys` collection + `IsComplete` derived (`Keys.Count(k => k.Status == Assigned) == Quantity`); `Order.AttachKey` (all-or-nothing → `Delivered`, bumps `UpdatedAt` per ADR-05).
- [x] 2.6 (remainder) `tests/Maxkeys.Domain.Tests/Orders/OrderTests.cs`: `AttachKey` all-or-nothing (2 of 3 keys stays `AwaitingFulfillment`, 3rd flips `Delivered` once — fulfillment spec `All-or-Nothing Delivery Derivation`); variant mismatch → `DomainException`; wrong-state attach → `DomainConflictException`.
- [x] 2.7 `tests/Maxkeys.Domain.Tests/Keys/KeyTests.cs`: `Key.AssignTo` transition.
- Test: `dotnet test tests/Maxkeys.Domain.Tests` green (56/56).

## Phase 2: Infrastructure (EF + migrations, KeyCipher)

**PR3a** — `feat/mvp-03-ef-persistence` — base: PR2b → `main` after merge — depends on: PR2b — 362 lines (actual)

- [x] 3.1 Create `src/Maxkeys.Infrastructure/Maxkeys.Infrastructure.csproj` (Npgsql, `EFCore.NamingConventions`) and `src/Maxkeys.Application/Persistence/IAppDbContext.cs` (`DbSet<Product/ProductVariant/Order/OrderItem/Key/OutboxEvent/ProcessedWebhookNotification>`, `SaveChangesAsync`).
- [x] 3.2 Create `src/Maxkeys.Infrastructure/Persistence/AppDbContext.cs : DbContext, IAppDbContext` with snake_case naming (ADR-16).
- [x] 3.3 Create `Configurations/*.cs` (Product, ProductVariant, Order [`xmin` via `Property<uint>("xmin").IsRowVersion()` — `UseXminAsConcurrencyToken()` is obsolete in this Npgsql provider version], OrderItem, Key [`bytea EncryptedCode`], OutboxEvent [`jsonb Payload`], ProcessedWebhookNotification [PK=`request_id`]) with indexes per design §5 (`products.slug` UNIQUE, `orders.user_id`/`status`, `UNIQUE(mp_payment_id) WHERE NOT NULL`, `outbox_events(status, next_attempt_at)`, `keys(order_item_id)`, `keys(product_variant_id, status)`, `product_variants(product_id)`).
- Test: `dotnet build Maxkeys.sln` green, 0 warnings (`TreatWarningsAsErrors`). **PR3 split (auto-chain)**: implementation measured 600 authored lines (excluding the generated migration) against the 400-line cap, so the slice was split into PR3a `feat/mvp-03-ef-persistence` (tasks 3.1–3.3, 362 lines) and PR3b `feat/mvp-03b-ef-testcontainers` (tasks 3.4–3.6, 238 lines + excluded migration), stacked-to-main — same pattern as the PR1a/PR1b split. PR4 now bases on PR3b.

**PR3b** — `feat/mvp-03b-ef-testcontainers` — base: PR3a → `main` after merge — depends on: PR3a — 238 lines (actual, + generated migration excluded from budget)

- [x] 3.4 Generate `dotnet ef migrations add InitialCreate` into `Infrastructure/Persistence/Migrations/` (via a local `dotnet-ef` tool, `.config/dotnet-tools.json`, and a `DesignTimeDbContextFactory` reading `ConnectionStrings__Default` with a local fallback).
- [x] 3.5 Create `tests/Maxkeys.Application.Tests/Fixtures/PostgresFixture.cs` — Testcontainers Postgres. **Requires Docker locally/CI**; honors `TEST_POSTGRES_CONNECTION` env override when Docker is unavailable (ADR-09), documented in the fixture's XML doc comment; `PostgresCollection` shares one container across the test class.
- [x] 3.6 `tests/Maxkeys.Application.Tests/Persistence/AppDbContextTests.cs`: migration applies cleanly (queryable context); `slug` and `mp_payment_id` unique constraints enforced at the DB level (`DbUpdateException`); `Order` + `OrderItem` + `Key` (bytea) round-trip through the database; `OutboxEvent` jsonb payload round-trips semantically.
- Test: `dotnet test tests/Maxkeys.Application.Tests` green against a real container — 5/5. `dotnet test tests/Maxkeys.Domain.Tests` green — 56/56 (no regressions).

**PR4** — `feat/mvp-04-key-cipher` — base: `feat/mvp-03b-ef-testcontainers` → `main` after merge — depends on: PR3b — ~150 lines

- [x] 4.1 Add `src/Maxkeys.Application/Security/KeyCipherOptions.cs` (`Keys:EncryptionKey` base64/32 bytes, `Keys:CurrentVersion`), validated at startup (fail fast).
- [x] 4.2 Add `src/Maxkeys.Application/Security/KeyCipher.cs`: `Encrypt(string) -> (byte[] blob, short version)` / `Decrypt(byte[], short) -> string` via `AesGcm`, random 12-byte nonce, 16-byte tag, layout `nonce|tag|ciphertext` (fulfillment spec `Key Encryption at Rest`).
- [x] 4.3 `tests/Maxkeys.Application.Tests/Security/KeyCipherTests.cs`: round-trip; ciphertext ≠ plaintext; tampered tag/wrong version fails.
- Test: `dotnet test tests/Maxkeys.Application.Tests --filter KeyCipher` green — 8/8.

## Phase 3: Application

**PR5 split (auto-chain)**: PR5's estimate (~350 lines) plus the payment gateway seam and its fake ran well over the 400-line cap once catalog querying, image URL composition and the full test coverage were accounted for. First measured as one slice (580 actual lines), then split by orchestrator decision into three stacked slices: PR5a `feat/mvp-05a-catalog-application` (task 5.1 part: `CatalogDtos.cs`/`StorageOptions.cs`/`GetCatalog.cs` + listing/filter/search/imageUrl tests, 336 actual lines), PR5a2 `feat/mvp-05a2-product-detail-gateway` (task 5.1 remainder: `GetProductBySlug.cs` + detail tests, plus tasks 5.2/5.3: `IPaymentGateway`/`PaymentGatewayException`/`FakePaymentGateway`, 268 actual lines), and PR5b `feat/mvp-05b-checkout-application` (tasks 5.4-5.6, `CreateOrder`/`GetOrderStatus`, not started). All stacked-to-main. PR6 now bases on PR5b.

**PR5a** — `feat/mvp-05a-catalog-application` — base: PR4 → `main` after merge — depends on: PR4 — 336 lines (actual)

- [x] 5.1 (part) Add `src/Maxkeys.Application/Catalog/CatalogDtos.cs` (all three DTO records — `ProductSummary`, `ProductVariantDetail`, `ProductDetail` — matching design §7 verbatim), `StorageOptions.cs` (`Storage:R2PublicBaseUrl` + `ImageUrlBuilder`/`AddStorageUrlBuilder`, ADR-12 — placed in Application, not Infrastructure as design §3 lists, because `GetCatalog`/`GetProductBySlug` consume it directly and Application must not reference Infrastructure per ADR-01), `GetCatalog.cs` (platform filter + text search — catalog spec `Product Listing`).
- Test: `tests/Maxkeys.Application.Tests/Catalog/CatalogTestData.cs` (shared seed helpers, reused by PR5a2), `GetCatalogTests.cs` (4 tests: active-only listing with `FromPrice`/`OldPrice` from the cheapest variant, platform filter, case-insensitive search, `imageUrl` composed from base URL + key). `dotnet build Maxkeys.sln` 0 warnings; `dotnet test tests/Maxkeys.Application.Tests --filter GetCatalogTests` green — 4/4.

**PR5a2** — `feat/mvp-05a2-product-detail-gateway` — base: PR5a → `main` after merge — depends on: PR5a — 268 lines (actual)

- [x] 5.1 (remainder) Add `src/Maxkeys.Application/Catalog/GetProductBySlug.cs` (404 unknown/inactive — catalog spec `Product Detail Lookup`; variant `Name` composed from `Region`/`Edition` since `ProductVariant` has no stored name column).
- [x] 5.2 Add `src/Maxkeys.Application/Payments/IPaymentGateway.cs`, `PaymentGatewayException.cs`.
- [x] 5.3 Add `tests/Maxkeys.Application.Tests/Fakes/FakePaymentGateway.cs`.
- Test: `tests/Maxkeys.Application.Tests/Catalog/GetProductBySlugTests.cs` (3 tests, reuses `CatalogTestData` from PR5a: detail with active variants ordered by `SortOrder`, unknown slug → null, inactive product → null). `dotnet build Maxkeys.sln` 0 warnings; `dotnet test tests/Maxkeys.Application.Tests --filter "GetProductBySlugTests|Payments"` green.
- **Known gap (not a task, see PR12 task 12.0)**: `ProductDetail.Description` is always `""` — the `Product` domain entity has no `Description` column even though the proposal's ERD included one.

**PR5b** — `feat/mvp-05b-checkout-application` — base: PR5a2 → `main` after merge — depends on: PR5a2 — ~200 lines

- [x] 5.4 Add `src/Maxkeys.Application/Checkout/CreateOrder.cs`: load active variants, recompute price/snapshots (cart-checkout spec `Server-Side Price Recomputation`), `Order.Create`, commit #1, `IPaymentGateway.CreatePreference`, commit #2 (design §6a); optional-bearer `UserId` linkage (cart-checkout spec `Guest and Authenticated Checkout`, auth spec `User Identity Linking`).
- [x] 5.5 Add `src/Maxkeys.Application/Checkout/GetOrderStatus.cs` (masked email, `lastPaymentAttemptStatus`).
- [x] 5.6 `tests/Maxkeys.Application.Tests/Checkout/CreateOrderTests.cs`: multi-item checkout (2 variants, one qty 2) → 1 order/2 items; price tampering ignored; inactive/unknown variant → 422; missing email guest checkout → 422; authenticated checkout with edited email → `UserId` set, email as submitted; preference item count == order item count.
- Test: `dotnet test tests/Maxkeys.Application.Tests --filter Checkout` green.

**PR6** — `feat/mvp-06-payment-webhook-application` — base: PR5b → `main` after merge — depends on: PR5b — 378 lines (actual)

- [x] 6.1 Add `src/Maxkeys.Application/Payments/ProcessPaymentNotification.cs`: dedupe by `request_id` (spec `Notification Deduplication`), authoritative fetch via `IPaymentGateway.GetPaymentAsync` (spec `Authoritative Payment Fetch`), state guard (spec `Idempotent State Transition`); branches — approved+match → `MarkPaid` + `OrderApproved` outbox row same tx (spec `Transactional Outbox Insert on Approval`); approved+mismatch → dedupe row + Warning log, no transition; rejected/pending/in_process → `RecordPaymentAttempt` only (spec `Rejected Payment Handling`); other statuses → generic ignored-status log (spec `Non-Actionable Status Handling`); unparseable/unknown `external_reference` → `OrderNotFound` outcome + dedupe row; a `DbUpdateException` on the combined save (order `xmin` conflict, `mp_payment_id` unique violation, or `request_id` PK violation) clears the change tracker and re-derives the outcome from the order's persisted status, guaranteeing exactly one transition and at most one outbox row for a concurrent duplicate delivery.
- [x] 6.2 `tests/Maxkeys.Application.Tests/Payments/ProcessPaymentNotificationTests.cs` (7 tests): same request id ×3 → one `Paid`, one outbox row, `GetPaymentAsync` called once; different request ids, order already `Paid` → no second transition/outbox row (spec `Re-notification after Paid`); concurrent duplicate approval (two request ids, two `DbContext`s, `Task.WhenAll`) → exactly one transition/one outbox row (spec `Concurrent duplicate delivery of the same approval`); amount/currency mismatch → ignored, no transition; `rejected` on `Pending` → `LastPaymentAttempt*` recorded, stays `Pending`, zero outbox rows, later `approved` still transitions once (spec `Rejection followed by a later approval`); `refunded` → log-only, no fields written, dedupe row present (spec `Refunded status ignored`); unknown external reference → `OrderNotFound`, dedupe row present.
- Test: `dotnet test tests/Maxkeys.Application.Tests --filter Payments` green — 7/7 (34/34 full Application suite; 56/56 Domain, no regressions). `dotnet build Maxkeys.sln` 0 warnings.

**PR8** — `feat/mvp-08-fulfillment-application` — base: PR7b → `main` after merge — depends on: PR4, PR7b — ~380 lines

- [ ] 8.1 Add `src/Maxkeys.Application/Fulfillment/AttachKeyToOrderItem.cs`: load order+items+keys (`xmin`), `KeyCipher.Encrypt`, `Key.Create` + `Order.AttachKey`, insert `OutboxEvent(OrderDelivered)` in the same tx as the last-key `Delivered` transition (fulfillment spec `Key Attachment`, `All-or-Nothing Delivery Derivation`); catch `DbUpdateConcurrencyException`, reload, retry once, else 409 (spec `Concurrent double-attach on the same item`).
- [ ] 8.2 Add `src/Maxkeys.Application/Fulfillment/ListOrdersAwaitingFulfillment.cs` (per-item assigned/required counts — spec `Admin Order Listing`).
- [ ] 8.3 Add `src/Maxkeys.Application/Outbox/OrderDeliveredHandler.cs : IOutboxHandler` — load order+items+keys, assert `Delivered`, `KeyCipher.Decrypt` each key in memory only, send one buyer email with all keys grouped by item.
- [ ] 8.4 Add buyer delivery email template to `EmailTemplates.cs`.
- [ ] 8.5 Add `src/Maxkeys.Application/Orders/GetMyOrders.cs` (owner-only — orders-history spec `My Orders Listing`), `GetMyOrder.cs` (keys only when `Delivered` — spec `Order Detail With Conditional Key Reveal`; 404 on non-owner — spec `Ownership Enforcement`, ADR-14).
- [ ] 8.6 **Spec carry-over** — reword `One-Time Delivery Email` in `specs/fulfillment/spec.md` to: "The system MUST send the delivery email containing all keys exactly once per `AwaitingFulfillment → Delivered` transition, emitted via the `OrderDelivered` outbox event inserted in the same transaction as that transition. Handler retries MAY duplicate the email if the send succeeds but marking the event `Processed` fails (at-least-once)." Keep the existing "Email sent once even under retry/re-evaluation" scenario unchanged.
- [ ] 8.7 `tests/Maxkeys.Application.Tests/Fulfillment/AttachKeyToOrderItemTests.cs`: over-quantity attach → 409; concurrent parallel attaches → one 409 then success on retry; last key → `Delivered` + one `OrderDelivered` row. `tests/Maxkeys.Application.Tests/Outbox/OrderDeliveredHandlerTests.cs`: email contains all keys once; re-evaluating an already-sent transition sends no second email (ADR-04). `tests/Maxkeys.Application.Tests/Orders/GetMyOrdersTests.cs`: owner scoping, cross-user 404.
- Test: `dotnet test tests/Maxkeys.Application.Tests --filter "Fulfillment|Orders"` green.

## Phase 4: API

**PR9** — `feat/mvp-09-api-skeleton` — base: PR8 → `main` after merge — depends on: PR5, PR7b — ~370 lines

- [ ] 9.1 Add `src/Maxkeys.Api/Maxkeys.Api.csproj`, `Program.cs` (minimal API bootstrap, DI via `Infrastructure/DependencyInjection.cs`), `appsettings.json`, `appsettings.Development.json`.
- [ ] 9.2 Add `src/Maxkeys.Api/Errors/ProblemDetailsExceptionHandler.cs`: `DomainException`→422, `DomainConflictException`→409, `NotFoundException`→404, `PaymentGatewayException`→503, unhandled→500 (design §7 error table).
- [ ] 9.3 Add `src/Maxkeys.Api/Logging/SerilogSetup.cs`, `CorrelationIdMiddleware.cs`, `SensitiveDataPolicy.cs` (masks `Key`, `AttachKeyRequest`, `OrderDetail` — design §8/§12).
- [ ] 9.4 Add `src/Maxkeys.Api/Endpoints/HealthEndpoints.cs` (`/health` DB check, `/health/live` process only).
- [ ] 9.5 Add `src/Maxkeys.Api/Endpoints/CatalogEndpoints.cs` (`GET /catalog/products`, `GET /catalog/products/{slug}`).
- [ ] 9.6 Add `src/Maxkeys.Api/Endpoints/CheckoutEndpoints.cs` (`POST /checkout/orders`, `GET /checkout/orders/{id}/status`). **HTTP body model MUST NOT bind `UserId` — derive it from the optional bearer `sub` only; body is `{email, items:[{variantId, quantity}]}` (design 7, frontend `types/api.ts`).**
- [ ] 9.7 Add `src/Maxkeys.Infrastructure/Payments/NotConfiguredPaymentGateway.cs` — registered as `IPaymentGateway` when `Payments:AccessToken` is empty; throws `PaymentGatewayException` → 503, so `POST /checkout/orders` builds and runs green without MP credentials.
- [ ] 9.8 Add `Cors:AllowedOrigins` option (array, `src/Maxkeys.Api/Program.cs` or a dedicated `CorsOptions.cs`) and wire `UseCors` in `Program.cs` — configuration-driven allowlist (e.g. `http://localhost:3000` in dev, the Vercel production origin in prod), no wildcard origin ever registered (design §10/§12, ADR-18 CORS consequence).
- [ ] 9.9 Add `Dockerfile` (multi-stage `sdk:8.0` → `aspnet:8.0`, non-root, port 8080).
- [ ] 9.10 `tests/Maxkeys.Api.Tests/`: catalog endpoint scenarios (active-only, platform filter, search); Problem Details shape per status; CORS test — request from an origin not in `Cors:AllowedOrigins` receives no `Access-Control-Allow-Origin` header, an allowed origin does.
- Test: `dotnet build && dotnet test tests/Maxkeys.Api.Tests --filter "Catalog|Cors"` green with no MP credentials configured.

**PR10** — `feat/mvp-10-api-auth` — base: PR9 → `main` after merge — depends on: PR8, PR9 — ~380 lines

- [ ] 10.0 **BLOCKING — Confirm Supabase project JWT signing mode (asymmetric JWKS vs legacy HS256) in the Supabase dashboard (Auth → JWT keys) and set `Auth:Mode` accordingly before implementing the rest of this PR.**
- [ ] 10.1 Add `src/Maxkeys.Api/Auth/AuthOptions.cs` (`Auth:Mode`, `Issuer`, `Audience`, `JwksUrl`, `Hs256Secret`, `AdminSubs`).
- [ ] 10.2 Add `src/Maxkeys.Api/Auth/JwksKeyCache.cs` (cache 10 min, refetch on unknown `kid` once), `JwtSetup.cs` (JWKS or HS256 branch by `Auth:Mode`, `MapInboundClaims=false` — auth spec `Configurable JWT Validation Mode`).
- [ ] 10.3 Add `src/Maxkeys.Api/Auth/AdminPolicy.cs` (`sub` in `Auth:AdminSubs`; empty allowlist → always deny — auth spec `Admin Authorization Policy`, fulfillment spec `Admin Authorization`).
- [ ] 10.4 Add `src/Maxkeys.Api/Auth/OptionalBearerFilter.cs` (present-but-invalid bearer on `POST /checkout/orders` → 401, not a silent guest order — design §6e).
- [ ] 10.5 Add `src/Maxkeys.Api/Endpoints/MeEndpoints.cs` (`GET /me/orders`, `GET /me/orders/{id}`), `AdminEndpoints.cs` (`GET /admin/orders?status=AwaitingFulfillment`, `POST /admin/orders/{id}/items/{itemId}/keys`).
- [ ] 10.6 `tests/Maxkeys.Api.Tests/Auth/`: HS256 mode with a locally signed token → 200; JWKS mode with a test key pair served from an in-process endpoint → 200; wrong `aud` → 401; expired/malformed/unknown-kid → 401 (auth spec `Invalid or expired token rejected`); `/admin/*` non-allowlisted → 403, empty allowlist → 403; `/me/orders/{id}` other owner → 404 (orders-history spec `Cross-user access denied`); anonymous `/me/orders` → 401; anonymous checkout allowed. **Requires Docker/Testcontainers per PR3; honor `TEST_POSTGRES_CONNECTION`.**
- Test: `dotnet test tests/Maxkeys.Api.Tests --filter Auth` green.

## Phase 5: Mercado Pago integration + outbox hosted service + email

**PR7a** — `feat/mvp-07a-outbox-handler-email` — base: PR6 → `feat/mvp-07b-outbox-processor` — depends on: PR3 — 361 lines (actual)
*(chain position 7a — outbox handler seam + email seam only; the poller itself is PR7b. See PR Chain Order table above for the authoritative sequence.)*

- [x] 7.1 Add `src/Maxkeys.Application/Outbox/IOutboxHandler.cs` (`string EventType`, `HandleAsync`), `OrderApprovedHandler.cs` (`Paid` → `AwaitingFulfillment` + operator notification — outbox-processing spec `OrderApproved Handler`). Also added `OutboxServiceCollectionExtensions.AddOutboxHandlers` so PR7b's processor can resolve `IEnumerable<IOutboxHandler>`.
- [x] 7.2 Add `src/Maxkeys.Application/Notifications/IEmailSender.cs`, `EmailMessage.cs`, `EmailTemplates.cs` (operator "Order X awaiting fulfillment" template), `EmailOptions.cs` (`Email:Sender`, `Email:From`, `Email:OperatorTo`).
- [x] 7.3 Add `src/Maxkeys.Infrastructure/Email/LoggingEmailSender.cs` (dev default), `tests/Maxkeys.Application.Tests/Fakes/RecordingEmailSender.cs`.
- [x] 7.6a `tests/Maxkeys.Application.Tests/Outbox/OrderApprovedHandlerTests.cs`: `Paid` → `AwaitingFulfillment`, exactly one operator notification recorded via `RecordingEmailSender` (no key material in it); already `AwaitingFulfillment` → no-op, no email; unknown order → throws (lets the future processor retry/dead-letter).
- Test: `dotnet test tests/Maxkeys.Application.Tests --filter Outbox` green — 4/4 (3 new + 1 pre-existing `AppDbContextTests` outbox jsonb round-trip). Full suite 37/37. `dotnet build Maxkeys.sln` 0 warnings.

**PR7b** — `feat/mvp-07b-outbox-processor` — base: PR7a → `main` after merge — depends on: PR3, PR7a — 400 lines (actual)

- [x] 7.4 Add `src/Maxkeys.Infrastructure/Outbox/OutboxClaimQuery.cs` (`SELECT ... FOR UPDATE SKIP LOCKED`, marks `Processing` same tx — spec `Batch Claim With Row Locking`), `OutboxOptions.cs` (`PollIntervalSeconds`=5/`BatchSize`=20/`LeaseSeconds`=60/`MaxAttempts`=8, validated `>= 1`).
- [x] 7.5 Add `src/Maxkeys.Infrastructure/Outbox/OutboxProcessor.cs : BackgroundService` — poll loop (re-polls immediately on a full claimed batch, else waits `PollIntervalSeconds`), per-batch scope, claim, dispatch by `EventType` (resolves `IEnumerable<IOutboxHandler>` registered via PR7a's `AddOutboxHandlers`), `MarkProcessed`/`MarkFailedAttempt` per event with its own `SaveChangesAsync` (spec `Exponential Backoff on Failure`, `Dead-Letter After Max Attempts`), missing handler treated as a failure, `OperationCanceledException` handled gracefully on shutdown, unexpected exceptions logged and never kill the loop. `internal Task ProcessOnceAsync(CancellationToken)` + `InternalsVisibleTo("Maxkeys.Application.Tests")` lets tests drive one pass directly. `AddOutboxProcessor(IServiceCollection, IConfiguration)` registers options + validator + `OutboxClaimQuery` + the hosted service.
- [x] 7.6b `tests/Maxkeys.Application.Tests/Outbox/OutboxClaimQueryTests.cs` (2 tests): two concurrent claimers (two `AppDbContext`s, `Task.WhenAll`) never claim the same row and together cover every seeded row (spec `Multi-Instance Claim Safety`); expired `Processing` lease reclaimable, future lease and `Failed` rows excluded. `tests/Maxkeys.Application.Tests/Outbox/OutboxProcessorTests.cs` (2 tests, `FakeOutboxHandler`): failing handler → `Pending`, `Attempts=1`, `NextAttemptAt≈+60s`, then (prior failures simulated directly via the domain API, far in the past, to avoid waiting through real backoff) dead-letters to `Failed` at `MaxAttempts`; succeeding handler → `Processed`.
- Test: `dotnet test tests/Maxkeys.Application.Tests --filter Outbox` green — 8/8 (4 new OutboxClaimQueryTests+OutboxProcessorTests + 4 pre-existing OrderApprovedHandlerTests). Full Application suite 41/41. Domain 56/56. `dotnet build Maxkeys.sln` 0 warnings. Runtime harness: `OutboxProcessorTests` drives `OutboxProcessor.ProcessOnceAsync` directly against Testcontainers Postgres (one real poll-cycle pass per test, not the timed loop).

**PR11** — `feat/mvp-11-mercadopago-integration` — base: PR10 → `main` after merge — depends on: PR6, PR9 — ~350 lines

- [ ] 11.1 Add `src/Maxkeys.Infrastructure/Payments/MercadoPagoOptions.cs` (`Payments:AccessToken`, `WebhookSecret`, `WebhookEnabled`, `NotificationUrl`).
- [ ] 11.2 Add `src/Maxkeys.Infrastructure/Payments/MercadoPagoSignatureValidator.cs`: HMAC-SHA256 over `id:<data.id>;request-id:<x-request-id>;ts:<ts>;`, hex-compare via `CryptographicOperations.FixedTimeEquals` (payments-webhook spec `Signature Validation Before Any I/O`).
- [ ] 11.3 Add `src/Maxkeys.Infrastructure/Payments/MercadoPagoGateway.cs : IPaymentGateway` — typed `HttpClient`, `POST /checkout/preferences`, `GET /v1/payments/{id}` (ADR-10); DI selects this over `NotConfiguredPaymentGateway` when `Payments:AccessToken` is non-empty.
- [ ] 11.4 Add `src/Maxkeys.Api/Endpoints/WebhookEndpoints.cs` (`POST /webhooks/mercadopago`): `WebhookEnabled=false` → 503 before any other check (spec `Kill Switch`); else validate signature → 401 with zero I/O on failure; else `ProcessPaymentNotification.Execute`.
- [ ] 11.5 `tests/Maxkeys.Api.Tests/Webhooks/`: signature validator vectors — valid, tampered `v1`, missing header → 401 (spec `Invalid signature rejected`); `WebhookEnabled=false` → 503 with no state change; exact replay of the same `x-request-id` → 2xx, no state change (spec `Exact replay is a no-op`).
- [ ] 11.6 **Verify the `x-signature` manifest (segment order, lowercase `data.id`) against a real Mercado Pago sandbox notification** before merging; adjust `MercadoPagoSignatureValidator` if the sandbox payload differs from the documented manifest.
- Test: `dotnet test tests/Maxkeys.Api.Tests --filter Webhooks` green.

**PR12** — `feat/mvp-12-email-seed` — base: PR11 → `main` after merge — depends on: PR7a, PR9 — ~260 lines

- [ ] 12.0 Add `Product.Description` (Domain property + EF config `text` + additive migration `AddProductDescription`) and map it in `CatalogDtos`; the proposal ERD and the frontend `types/api.ts` `ProductDetail.description` require it (gap surfaced in PR5a2; `ProductDetail.Description` is currently always `""`).
- [ ] 12.1 Extend `src/Maxkeys.Application/Notifications/EmailOptions.cs` (already added in PR7a with `Sender`/`From`/`OperatorTo`) with `Smtp:Host/Port/UseStartTls/User/Password`. Note the property is named `OperatorTo`, not `OperatorAddress` as design section 10's config matrix names it — reconcile the doc or rename here, whichever this PR decides.
- [ ] 12.2 Add `src/Maxkeys.Infrastructure/Email/SmtpEmailSender.cs : IEmailSender` (MailKit, ADR-08); DI selects `Smtp` or `Logging` by `Email:Sender`.
- [ ] 12.3 Add `src/Maxkeys.Infrastructure/Persistence/CatalogSeeder.cs`, `seed/catalog.json`; wire `Maxkeys.Api --seed-catalog <path>` in `Program.cs` (ADR-12, upsert by slug).
- [ ] 12.4 Add `src/Maxkeys.Infrastructure/Storage/StorageOptions.cs` (`Storage:R2PublicBaseUrl`), `R2ImageUrlResolver.cs` (catalog spec `Image URL Resolution`).
- [ ] 12.5 `tests/Maxkeys.Application.Tests/Catalog/CatalogQueriesTests.cs`: image URL resolved to absolute, not raw key; seeder upsert idempotent on re-run.
- Test: `dotnet test tests/Maxkeys.Application.Tests --filter Catalog` green.

## Phase 6: Frontend (repo `maxkeys-front`, independent of backend PR1‑PR12)

**PR13** — `feat/mvp-13-frontend-scaffold` — repo: `maxkeys-front` — base: own initial commit → `main` — depends on: — — ~380 lines

- [x] 13.1 Bootstrap the `maxkeys-front` repo: `git init -b main` in `C:\Personal\Projects\maxkeys-front`.
- [x] 13.2 Create `.gitignore` (`node_modules/`, `.nuxt/`, `.output/`, `dist/`, `.env`, `.env.local`).
- [x] 13.3 Create `README.md` linking to `maxkeys-back/openspec/changes/mvp-marketplace/` (specs + design section 7) as the API contract source.
- [x] 13.4 Commit the bootstrap as the initial commit on `main` (`chore: initial commit (Nuxt scaffold bootstrap)`); add remote `git remote add origin https://github.com/AlanMartinez/maxkey-front.git`; push (`git push -u origin main`).
- [x] 13.5 `nuxt.config.ts` (modules `@nuxtjs/supabase` [`redirect:false`], `@nuxtjs/tailwindcss`; `runtimeConfig.public.apiBaseUrl`, `siteUrl`).
- [x] 13.6 `tailwind.config.ts` (tokens: `colors.bg #0A0A0E`, `colors.surface #12121A`, `colors.accent {DEFAULT:#7C5CFC, hover:#8F6FFF}`, `colors.success #22D3A8`, `fontFamily.display=['Space Grotesk']`, `fontFamily.sans=['Inter']`, `backdropBlur.glass=12px`).
- [x] 13.7 `assets/css/main.css`, `app.vue` (`AppHeader` + `<NuxtPage>` + `CartDrawer` + `LoginDialog`).
- [x] 13.8 `composables/useApi.ts` (`$fetch.create` with `baseURL` from `runtimeConfig.public.apiBaseUrl`, fed by env `NUXT_PUBLIC_API_BASE_URL`; bearer attach on request, `ApiError` on response error).
- [x] 13.9 `types/api.ts` — DTOs mirrored by hand from design section 7 (catalog + checkout DTOs first); each type carries a comment citing the section 7 row it mirrors (e.g. `// mirrors design.md §7 "GET /catalog/products" response`), per the cross-repo contract (ADR-18).
- [x] 13.10 `components/layout/AppHeader.vue`, `AppFooter.vue`; `components/ui/AppButton.vue`, `AppBadge.vue`, `Skeleton.vue`, `EmptyState.vue`, `ErrorState.vue`.
- [x] 13.11 `.env.example` (`NUXT_PUBLIC_API_BASE_URL`, `NUXT_PUBLIC_SITE_URL`, `SUPABASE_URL`, `SUPABASE_KEY`).
- Test: `npm run test && nuxi typecheck` green.

**PR14** — `feat/mvp-14-frontend-catalog` — repo: `maxkeys-front` — base: PR13 → `main` after merge — depends on: PR13 — ~350 lines

- [x] 14.1 `components/catalog/HeroCarousel.vue`, `PlatformFilter.vue`, `ProductCard.vue`, `ProductGrid.vue`.
- [x] 14.2 `components/product/VariantSelector.vue` (emits selected variant), `TrustBadges.vue`.
- [x] 14.3 `pages/index.vue` (catalog via `useAsyncData` + `useApi`, `GET /catalog/products` — catalog spec `Product Listing`, `Filter by platform`, `Search by name`), `pages/product/[slug].vue` (`GET /catalog/products/{slug}` — catalog spec `Product Detail Lookup`).
- [x] 14.4 `tests/VariantSelector.spec.ts`: emits the selected variant on click.
- Test: `npm run test -- VariantSelector` green.

**PR15** — `feat/mvp-15-frontend-cart` — repo: `maxkeys-front` — base: PR14 → `main` after merge — depends on: PR13 — ~300 lines

- [x] 15.1 `composables/useCart.ts`: `useState<CartState>`, `CartLine` shape, computed `count`/`subtotal`, actions `add`/`remove`/`setQuantity` (clamp 1..10), `clear`; persisted to `localStorage['nexo.cart.v1']`, hydrated `onMounted`.
- [x] 15.2 `components/cart/CartDrawer.vue`, `CartLine.vue`.
- [x] 15.3 `tests/useCart.spec.ts`: add/merge same variant, `setQuantity` clamps to 1..10, persistence round-trip, `clear`.
- Test: `npm run test -- useCart` green.

**PR16** — `feat/mvp-16-frontend-checkout` — repo: `maxkeys-front` — base: PR15 → `main` after merge — depends on: PR15 — ~300 lines

- [x] 16.1 `composables/useCheckout.ts`: `status: idle|submitting|redirecting|error`; `submit(email)` → `POST /checkout/orders`, `sessionStorage['nexo.lastOrderId']`, redirect to `initPoint`.
- [x] 16.2 `components/checkout/ContactForm.vue`, `OrderSummary.vue`, `PayWithMercadoPago.vue`.
- [x] 16.3 `pages/checkout/index.vue`, `pages/checkout/result.vue` (polls `GET /checkout/orders/{id}/status` every 3s up to 20 tries; clears cart only when `status !== 'Pending'` or MP query `status=approved`).
- [x] 16.4 `tests/useCheckout.spec.ts`: request body shape (`variantId`, `quantity` pairs + email); state machine transitions.
- Test: `npm run test -- useCheckout` green.

**PR17** — `feat/mvp-17-frontend-auth-orders` — repo: `maxkeys-front` — base: PR16 → `main` after merge — depends on: PR14 — ~380 lines

- [x] 17.1 `composables/useAuth.ts`: wraps `useSupabaseClient()`/`useSupabaseUser()`, `signInWithGoogle()` (`redirectTo=${siteUrl}/auth/callback`), `signOut()`.
- [x] 17.2 `components/layout/LoginDialog.vue`, `middleware/auth.ts` (redirect cookie `nexo.redirect` + `navigateTo('/?login=1')` when unauthenticated), `pages/auth/callback.vue`.
- [x] 17.3 `components/orders/OrderCard.vue`, `OrderStatusBadge.vue`, `KeyReveal.vue` (reveal/copy, rendered only when order `Delivered`).
- [x] 17.4 `pages/account/orders/index.vue` (`middleware:'auth'`, `GET /me/orders`), `pages/account/orders/[id].vue` (`GET /me/orders/{id}`, `KeyReveal` per item only when `Delivered` — orders-history spec `Order Detail With Conditional Key Reveal`).
- [x] 17.5 Add order-detail DTOs to `types/api.ts`, citing the design section 7 rows they mirror (`GET /me/orders`, `GET /me/orders/{id}`).
- [x] 17.6 Manual check: confirm `KeyReveal` renders no key codes for a non-`Delivered` order fixture (orders-history spec `Non-delivered order hides keys`).
- Test: `npm run test` (full suite) green.

## Phase 7: Deploy/runbook

**PR18a** — `feat/mvp-18a-deploy-runbook` — repo: `maxkeys-back` — base: `main` (after PR11 and PR12 merged) — depends on: PR11, PR12 — ~180 lines

- [ ] 18a.1 `deploy/fly.toml`, `deploy/railway.json` skeletons (env keys per design §10; `release_command`/pre-deploy running `Maxkeys.Api --migrate`).
- [ ] 18a.2 Add `--migrate` flag handling in `src/Maxkeys.Api/Program.cs` (`Database.Migrate()` then exit — ADR-13).
- [ ] 18a.3 Set production `Cors:AllowedOrigins` values (Vercel production origin + preview origins as needed) in `deploy/fly.toml`/`railway.json` env config.
- [ ] 18a.4 Write the sandbox end-to-end runbook at `docs/runbook-sandbox.md` (proposal Success Criteria): browse → variant → 2-item cart (one qty 2) → guest checkout → MP sandbox approval → webhook processed once → `AwaitingFulfillment` → operator attaches 3 keys → `Delivered` → one email → keys visible in Mis compras.
- [ ] 18a.5 Manually execute the sandbox runbook once against real MP sandbox + a test Supabase user; record pass/fail in the runbook doc.
- Test: `dotnet test` green; runbook executed manually (no automated harness — real MP sandbox required).

**PR18b** — `feat/mvp-18b-deploy-config` — repo: `maxkeys-front` — base: `main` (after PR17 merged) — depends on: PR17 — ~80 lines

- [x] 18b.1 Add Vercel config (`vercel.json` if needed) for the Nitro preset (SSR), repo root as the project root (no root-directory override).
- [x] 18b.2 Finalize `.env.example` and document each var in `README.md` (`NUXT_PUBLIC_API_BASE_URL` pointed at the backend deploy URL, `NUXT_PUBLIC_SITE_URL`, `SUPABASE_URL`, `SUPABASE_KEY`).
- [x] 18b.3 Note in `README.md` that Vercel preview deployments get their own origin and must be added to the backend's `Cors:AllowedOrigins` (PR18a) before they can call the API.
- Test: `npm run test` green; `npm run build` + a Vercel preview deploy reaches the running backend (manual check, requires PR18a deployed).
