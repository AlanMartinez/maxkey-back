# Proposal: Admin Catalog Full CRUD (Create/Soft-Delete + Discount Pricing)

## Intent

Admin catalog management is read+update only (`GET /admin/catalog/products`, `PUT .../products/{id}`, `PUT .../variants/{id}`). Adding a product or a new sellable variant still requires editing `CatalogSeeder` JSON and redeploying (ADR-12 legacy), and pricing is expressed as a raw `OldPrice` that the admin must back-compute. Admins need to run the catalog fully from the dashboard: add/remove products, add/remove variants per product, and set price, description, currency and discount percentage without a redeploy.

## Scope

### In Scope
- `POST /admin/catalog/products` — create (409 on duplicate slug, pre-checked before the unique index).
- `DELETE /admin/catalog/products/{id}` — soft-delete (`IsActive=false`), returns updated `AdminProduct`.
- `POST /admin/catalog/products/{productId}/variants` — create variant (404 if product missing).
- `DELETE /admin/catalog/variants/{id}` — soft-delete, returns updated `AdminVariant`.
- `PUT /admin/catalog/variants/{id}` — `discountPercentage` replaces `oldPrice` as input.
- Domain: currency whitelist `ARS|USD`; `DiscountPercentage` (nullable, `0 < pct < 100`) replaces persisted `OldPrice`.
- One EF migration (add `DiscountPercentage`, drop `OldPrice`), plus seeder/DTO/test alignment.

### Out of Scope
- Frontend/Nuxt work (handled in parallel in `maxkeys-front-fe`).
- Hard delete, slug editing, image upload flow, bulk import, multi-currency FX or checkout currency handling.
- Any change to `AdminPolicy`, payments, or auth.

## Capabilities

### New Capabilities
- None

### Modified Capabilities
- `admin-catalog`: add product/variant creation, soft-delete semantics, currency whitelist, and discount-percentage pricing with computed `OldPrice`.

## Locked Constraints (decided, not open)

| # | Constraint |
|---|------------|
| 1 | Soft-delete only, always. `DELETE` is a distinct REST action that sets `IsActive=false`. No hard row delete, no conditional branching — protects `Key.ProductVariantId` / `OrderItem.ProductVariantId`. |
| 2 | Currency is a fixed whitelist `["ARS","USD"]`, replacing the hardcoded `Currency != "ARS"` check. Not free-form ISO 4217. |
| 3 | `DiscountPercentage` is the only persisted discount input. `OldPrice` becomes computed in read DTO mapping: `Price / (1 - pct/100)`, else `null`. Never persist both (drift tradeoff). |
| 4 | No new domain mutation methods. `DeleteProduct` / `DeleteProductVariant` are thin use cases reusing `Product.UpdateCatalogInfo` / `ProductVariant.UpdateDetails` with `isActive=false`. |
| 5 | Backend-only change (`Maxkeys.*`). |

## Approach

Extend the existing vertical slice pattern (one class per use case, ADR-02): add `CreateProduct`, `DeleteProduct`, `CreateProductVariant`, `DeleteProductVariant` in `Maxkeys.Application/Catalog`, register them in DI, and map four new routes in the existing `AdminPolicy` group. Domain change is localized to `ProductVariant` (whitelist + `DiscountPercentage` invariant); `OldPrice` moves from a persisted column to a mapping-time computation shared by admin and public read DTOs.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `src/Maxkeys.Domain/Catalog/ProductVariant.cs` | Modified | Currency whitelist; `DiscountPercentage` replaces `OldPrice` in ctor + `UpdateDetails` |
| `src/Maxkeys.Application/Catalog/` | New | `CreateProduct`, `DeleteProduct`, `CreateProductVariant`, `DeleteProductVariant` |
| `src/Maxkeys.Application/Catalog/` (existing) | Modified | `ListAdminProducts`, `UpdateProduct`, `UpdateProductVariant`, `GetCatalog`/`GetProductBySlug` DTO mapping computes `OldPrice` |
| `src/Maxkeys.Api/Endpoints/AdminCatalogEndpoints.cs` | Modified | 4 new routes + request records; variant PUT body field swap |
| `src/Maxkeys.Infrastructure/Persistence/` | Modified | `ProductVariantConfiguration`, `CatalogSeeder`, new EF migration |
| `tests/` | Modified | Domain, Application and API test suites touching variant pricing |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Dropping `OldPrice` column loses existing discount data | Med | Migration backfills `DiscountPercentage` from `(1 - Price/OldPrice) * 100` before dropping the column |
| Public catalog response shape changes (`oldPrice` now computed) | Med | Field name and type stay identical; frontend contract already aligned in parallel session |
| Broad test churn across 3 suites from the domain signature change | Med | Single coordinated slice; `dotnet test` gates before delivery |
| Soft-deleted items still referenced by Keys/Orders | Low | Intentional — soft-delete preserves referential integrity by design |
| Payments / auth risk flag (`config.yaml` rule) | — | **Not triggered**: catalog + existing `AdminPolicy` only; no Mercado Pago or Supabase JWT/JWKS surface touched |

## Rollback Plan

Additive change plus one EF migration. Rollback: (1) `dotnet ef database update <previous-migration>` to restore the `OldPrice` column via the migration's `Down` (which recomputes `OldPrice` from `DiscountPercentage`), (2) `git revert` the endpoint, use-case and domain commits, (3) redeploy. No payment, auth, or order data is touched, so no data-repair step is required.

## Dependencies

- Frontend contract alignment in `maxkeys-front-fe` (parallel, non-blocking for backend merge).
- Existing `AdminPolicy` and `Auth:AdminSubs` configuration (reused, unchanged).

## Success Criteria

- [ ] An admin can create a product, create variants under it, and soft-delete either, entirely via the API with no redeploy.
- [ ] Variant price, description, currency (`ARS`/`USD`) and discount percentage are editable; invalid currency or `pct` outside `(0,100)` returns 422 Problem Details.
- [ ] `OldPrice` is absent from every write contract and consistently computed on every read contract.
- [ ] Soft-deleted products/variants disappear from public catalog responses but remain resolvable by existing `Key`/`OrderItem` references.
- [ ] `dotnet build` and `dotnet test` pass.
