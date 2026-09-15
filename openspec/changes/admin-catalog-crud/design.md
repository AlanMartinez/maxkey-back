# Design: Admin Catalog Full CRUD (Create/Soft-Delete + Discount Pricing)

## Technical Approach

Four new vertical slices (ADR-02, one class per use case) in `Maxkeys.Application/Catalog`, four routes on the existing `AdminPolicy` group, and one localized `ProductVariant` domain change: persisted `OldPrice` becomes persisted `DiscountPercentage`, and `OldPrice` becomes a read-time computation shared by admin and public DTO mapping. Domain stays framework-agnostic; EF Core touches only `ProductVariantConfiguration` plus one hand-edited migration. Flows are plain CRUD — no sequence diagram warranted.

## Architecture Decisions

### Decision: `OldPrice` computed in the read layer, not on the entity

| Option | Tradeoff | Decision |
|---|---|---|
| Drop the property; compute in DTO mapping via one shared helper | 6 call sites change; formula lives once | **Chosen** |
| Get-only computed `ProductVariant.OldPrice` | Zero call-site churn, but EF convention may try to map/ignore it, needing `builder.Ignore` and snapshot vigilance | Rejected |
| Keep both columns | Guaranteed drift between price and discount | Rejected (proposal constraint 3) |

**Rationale**: `OldPrice` is a presentation artifact of a discount, not state. One `public static class VariantPricing` in `Maxkeys.Application/Catalog` owns the formula:

```csharp
public static decimal? ComputeOldPrice(decimal price, decimal? discountPercentage) =>
    discountPercentage is null
        ? null
        : Math.Round(price / (1m - discountPercentage.Value / 100m), 2, MidpointRounding.AwayFromZero);
```

### Decision: currency whitelist as a private domain constant + shared `Validate`

`private static readonly string[] SupportedCurrencies = ["ARS", "USD"];` on `ProductVariant`, checked ordinally (no case normalization — matches today's `!= "ARS"`). Constructor and `UpdateDetails` both delegate to one `private static void Validate(price, discountPercentage, currency)`. Rejected: a `Currency` value object (over-engineering for two literals) and config-driven lists (would leak Infrastructure into Domain). **Rationale**: the spec requires *identical* create/update validation; today's duplicated invariant blocks that guarantee.

### Decision: `null` result means 409 for `CreateProduct`, 404 elsewhere

`CreateProduct.ExecuteAsync` returns `AdminProduct?`; `null` is only reachable via the slug pre-check, so the endpoint maps it to 409. Rejected: a new `ConflictException` (would require changing `ProblemDetailsExceptionHandler`, out of scope) and `DomainException` (maps to 422, wrong code). `CreateProductVariant` returns `AdminVariant?` with `null` = unknown parent → 404; this intentionally differs from `CreateCarouselSlide`, which throws `DomainException` (422), because the spec mandates 404.

### Decision: admin DTO mapping promoted to `public static` on `ListAdminProducts`

`ToAdminVariant` and a new `ToAdminProduct` become `public static`, mirroring the existing `ListCarouselSlides.ToAdminCarouselSlide` convention, and are reused by all seven admin catalog use cases instead of the current copy-paste in `UpdateProduct`.

## Data Flow

    POST/PUT/DELETE ─→ AdminCatalogEndpoints ─→ Use case ─→ ProductVariant (invariants)
                                                    │              │ DiscountPercentage persisted
                                                    ↓              ↓
                                            ListAdminProducts.ToAdminVariant ──→ VariantPricing.ComputeOldPrice
    GET /catalog/* ──→ GetCatalog / GetProductBySlug ─────────────────────────────────↑ (same formula)

## File Changes

| File | Action | Description |
|------|--------|-------------|
| `src/Maxkeys.Domain/Catalog/ProductVariant.cs` | Modify | `DiscountPercentage` replaces `OldPrice`; currency whitelist; shared `Validate` |
| `src/Maxkeys.Application/Catalog/VariantPricing.cs` | Create | Single `ComputeOldPrice` formula |
| `src/Maxkeys.Application/Catalog/CreateProduct.cs` | Create | Slug pre-check → `null`=409; returns `AdminProduct` |
| `src/Maxkeys.Application/Catalog/DeleteProduct.cs` | Create | `UpdateCatalogInfo(p.Name, p.Platform, p.Description, p.ImageKey, false)` |
| `src/Maxkeys.Application/Catalog/CreateProductVariant.cs` | Create | Parent lookup → `null`=404 |
| `src/Maxkeys.Application/Catalog/DeleteProductVariant.cs` | Create | `UpdateDetails(v.Price, v.DiscountPercentage, v.Currency, v.Region, v.Edition, v.SortOrder, false)` |
| `src/Maxkeys.Application/Catalog/ListAdminProducts.cs` | Modify | Mappers to `public static`; uses `VariantPricing` |
| `src/Maxkeys.Application/Catalog/UpdateProduct.cs` | Modify | Reuse shared mapper |
| `src/Maxkeys.Application/Catalog/UpdateProductVariant.cs` | Modify | `discountPercentage` param replaces `oldPrice` |
| `src/Maxkeys.Application/Catalog/GetCatalog.cs`, `GetProductBySlug.cs` | Modify | 3 `variant.OldPrice` reads → `VariantPricing.ComputeOldPrice(...)` |
| `src/Maxkeys.Api/Endpoints/AdminCatalogEndpoints.cs` | Modify | 4 routes + `CreateProductRequest`, `CreateProductVariantRequest`; `UpdateProductVariantRequest` field swap |
| `src/Maxkeys.Infrastructure/DependencyInjection.cs` | Modify | Register the 4 use cases in `AddUseCases` |
| `.../Configurations/ProductVariantConfiguration.cs` | Modify | `DiscountPercentage` `numeric(5,2)` replaces `OldPrice` `numeric(12,2)` |
| `.../Migrations/*_ReplaceVariantOldPriceWithDiscountPercentage.cs` | Create | Add → backfill → drop (below) |
| `src/Maxkeys.Infrastructure/Persistence/CatalogSeeder.cs` | Modify | `CatalogSeedVariant.DiscountPercentage` threaded to ctor/`UpdateDetails` |
| `seed/catalog.json` | Modify | `oldPrice` → `discountPercentage` (converted values) |
| `tests/Maxkeys.Application.Tests/Catalog/CatalogTestData.cs` | Modify | `SeedVariant` param swap |

## Interfaces / Contracts

```csharp
public sealed record CreateProductRequest(string Slug, string Name, string Platform, string? Description, string? ImageKey, bool IsActive);
public sealed record CreateProductVariantRequest(string? Region, string? Edition, decimal Price, decimal? DiscountPercentage, string Currency, int SortOrder, bool IsActive);
public sealed record UpdateProductVariantRequest(decimal Price, decimal? DiscountPercentage, string Currency, string? Region, string? Edition, int SortOrder, bool IsActive);
```

`POST` → `Results.Created($"/admin/catalog/{products|variants}/{id}", dto)`; `DELETE` → `Results.Ok(dto)` (not 204 — spec requires the updated DTO) or 404 Problem Details. `AdminVariant`/`ProductSummary`/`ProductDetail`/`ProductVariantDetail` keep `OldPrice` unchanged in name and type.

## Testing Strategy

| Layer | What to Test | Approach |
|-------|-------------|----------|
| Unit (Domain) | `0 < pct < 100` boundaries (0, 100, negative), `USD` accepted, `EUR` rejected, ctor/`UpdateDetails` parity | xUnit over `ProductVariant` |
| Integration (Application) | 4 new use cases; slug conflict → `null`; unknown parent → `null`; delete preserves sibling fields; `ComputeOldPrice` on admin and public reads | Existing `AppDbContext` test fixtures |
| API | 201/409/404/422 mapping; 401/403 on all 4 routes under `AdminPolicy` | `WebApplicationFactory` |
| Migration | Backfill parity: pre-migration `oldPrice` pair → post-migration computed `oldPrice` | Manual `dotnet ef database update` on a seeded local DB |

## Threat Matrix

N/A — no shell, subprocess, VCS/PR automation, executable-file classification, or process-integration boundary. New HTTP routes are added inside the existing `RequireAuthorization(AdminPolicy.Name)` group; 401/403 coverage is a required API test row above.

## Migration / Rollout

Scaffold, then hand-insert the SQL between `AddColumn` and `DropColumn` (EF generates only add/drop):

```csharp
// Up
AddColumn<decimal>("discount_percentage", "product_variants", "numeric(5,2)", nullable: true);
Sql(@"UPDATE product_variants SET discount_percentage = ROUND((1 - (price / old_price)) * 100, 2)
      WHERE old_price IS NOT NULL AND old_price > price;");
Sql(@"UPDATE product_variants SET discount_percentage = NULL
      WHERE discount_percentage IS NOT NULL AND (discount_percentage <= 0 OR discount_percentage >= 100);");
DropColumn("old_price", "product_variants");
// Down: re-add old_price numeric(12,2), SET old_price = ROUND(price / (1 - discount_percentage/100), 2)
//       WHERE discount_percentage IS NOT NULL, then drop discount_percentage.
```

The second `Up` statement guarantees every surviving row satisfies the new domain invariant (a rounded hairline discount cannot land on `0.00`). `Down` is rounding-lossy but restores every discount within one cent. Ordering: apply migration, then deploy code and re-seed — `seed/catalog.json` must be converted in the same PR, otherwise the next seeder run upserts `discountPercentage = null` and wipes the backfilled data.

## Open Questions

- [ ] Concurrent duplicate-slug creates bypass the pre-check and surface as `DbUpdateException` → 500. Accepted for single-digit admin concurrency; revisit only if a `ProblemDetailsExceptionHandler` unique-violation branch is later wanted.
