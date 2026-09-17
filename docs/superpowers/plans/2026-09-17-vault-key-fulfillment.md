# Vault Key Fulfillment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let admins bulk-preload key stock per product variant ("vault"), with a per-product active/inactive toggle, and auto-assign that stock to order items when a payment is approved — instead of requiring a manual key attach for every order.

**Architecture:** Reuses the existing `Key` entity (`Available`/`Assigned`) as the vault stock — no new entity. Adds `Product.VaultEnabled` (per-product toggle) and a concurrency token on `Key` (needed once multiple orders can race for the same available key). Extends `OrderApprovedHandler` to attempt auto-assignment via the existing `Order.AttachKey` domain method before falling back to the current manual-fulfillment email. New admin-only REST endpoints expose bulk key load, stock listing, and the toggle — backend only, no frontend in this repo.

**Tech Stack:** .NET 8, EF Core (Npgsql), xUnit + Testcontainers (real Postgres), ASP.NET Core Minimal APIs.

**Spec:** `docs/superpowers/specs/2026-09-17-vault-key-fulfillment-design.md`

## Global Constraints

- Backend only — this repo has no frontend. Do not create any frontend files.
- Toggle is per-product (`Product.VaultEnabled`), not per-variant.
- Partial stock is all-or-nothing per order item: if available keys < remaining quantity, skip that item entirely (no partial delivery).
- If vault auto-assignment fully delivers an order (`Order.Status -> Delivered`), do not send the operator "awaiting fulfillment" email.
- Follow existing patterns exactly: one class per use case (ADR-02), `IAppDbContext` as the only persistence seam from Application, admin endpoints require `AdminPolicy.Name`, `DomainException` (422) vs `DomainConflictException` (409) for validation vs. state conflicts.
- Tests use real Postgres via `PostgresFixture`/`PostgresCollection` (Application tests) and `Hs256ApiTestFixture`/`Hs256ApiCollection` (API tests) — never in-memory/SQLite.

---

### Task 1: `Product.VaultEnabled` domain flag

**Files:**
- Modify: `src/Maxkeys.Domain/Catalog/Product.cs`
- Test: `tests/Maxkeys.Domain.Tests/Catalog/ProductTests.cs`

**Interfaces:**
- Produces: `Product.VaultEnabled` (`bool`, defaults `false`), `Product.SetVaultEnabled(bool enabled)` (`void`) — consumed by Task 3 (`ToggleProductVault`), Task 5 (`ListVaultStock`), and Task 6 (`OrderApprovedHandler`).

- [ ] **Step 1: Write the failing tests**

Append to `tests/Maxkeys.Domain.Tests/Catalog/ProductTests.cs` (inside the existing `ProductTests` class, after `Constructor_WithValidData_CreatesActiveProduct`):

```csharp
    [Fact]
    public void Constructor_WithValidData_CreatesProductWithVaultDisabled()
    {
        var product = new Product("fc-points", "FC Points", "PS5");

        Assert.False(product.VaultEnabled);
    }

    [Fact]
    public void SetVaultEnabled_True_EnablesVault()
    {
        var product = new Product("fc-points", "FC Points", "PS5");

        product.SetVaultEnabled(true);

        Assert.True(product.VaultEnabled);
    }

    [Fact]
    public void SetVaultEnabled_False_DisablesVault()
    {
        var product = new Product("fc-points", "FC Points", "PS5");
        product.SetVaultEnabled(true);

        product.SetVaultEnabled(false);

        Assert.False(product.VaultEnabled);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Maxkeys.Domain.Tests --filter "FullyQualifiedName~ProductTests"`
Expected: FAIL — `VaultEnabled` and `SetVaultEnabled` do not exist on `Product`.

- [ ] **Step 3: Implement**

In `src/Maxkeys.Domain/Catalog/Product.cs`, add the property next to `IsActive`:

```csharp
    public bool IsActive { get; private set; }
    public bool VaultEnabled { get; private set; }
```

In the constructor, set it explicitly alongside the other properties (after `IsActive = isActive;`):

```csharp
        IsActive = isActive;
        VaultEnabled = false;
```

Add a new method after `RenameSlug`:

```csharp
    /// <summary>
    /// Enables or disables vault auto-fulfillment for this product (vault
    /// spec: Per-Product Vault Toggle). Applies uniformly to every variant of
    /// this product — there is no per-variant override.
    /// </summary>
    public void SetVaultEnabled(bool enabled)
    {
        VaultEnabled = enabled;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Maxkeys.Domain.Tests --filter "FullyQualifiedName~ProductTests"`
Expected: PASS (all `ProductTests`, including the 3 new ones).

- [ ] **Step 5: Commit**

```bash
git add src/Maxkeys.Domain/Catalog/Product.cs tests/Maxkeys.Domain.Tests/Catalog/ProductTests.cs
git commit -m "feat(vault): add Product.VaultEnabled toggle"
```

---

### Task 2: `Key` concurrency token + EF migration

**Files:**
- Modify: `src/Maxkeys.Infrastructure/Persistence/Configurations/KeyConfiguration.cs`
- Create: `src/Maxkeys.Infrastructure/Persistence/Migrations/<timestamp>_AddVaultSupport.cs` (generated)
- Create: `src/Maxkeys.Infrastructure/Persistence/Migrations/<timestamp>_AddVaultSupport.Designer.cs` (generated)
- Modify: `src/Maxkeys.Infrastructure/Persistence/Migrations/AppDbContextModelSnapshot.cs` (generated)

**Interfaces:**
- Produces: `Key` rows now carry an `xmin` concurrency token, so a `SaveChangesAsync` that updates a `Key` row already changed by another transaction throws `DbUpdateConcurrencyException` — relied on by Task 6's auto-assign retry.
- Produces: `products.vault_enabled` column (backs Task 1's `Product.VaultEnabled`).

This task has no unit test of its own — `Key`'s concurrency behavior is exercised end-to-end by Task 6's concurrency test, which needs this migration applied (the `PostgresFixture` runs `context.Database.MigrateAsync()` on every test run, so any pending migration is picked up automatically once generated).

- [ ] **Step 1: Add the concurrency token to `KeyConfiguration`**

In `src/Maxkeys.Infrastructure/Persistence/Configurations/KeyConfiguration.cs`, add after the existing `HasIndex` call:

```csharp
        builder.HasIndex(k => new { k.ProductVariantId, k.Status });

        builder.Property<uint>("xmin").IsRowVersion();
```

- [ ] **Step 2: Generate the migration**

From the repo root:

```bash
dotnet tool restore
dotnet ef migrations add AddVaultSupport --project src/Maxkeys.Infrastructure --startup-project src/Maxkeys.Api --output-dir Persistence/Migrations
```

This requires no live database (`DesignTimeDbContextFactory` only needs a connection string to *construct*, not connect, the `DbContextOptions`). Verify the generated migration's `Up` method adds both:
- `products.vault_enabled boolean NOT NULL DEFAULT FALSE` (from Task 1's `Product.VaultEnabled`)
- `keys.xmin` as a Postgres system column marked as the EF concurrency token (no explicit column add needed — `xmin` is a built-in Postgres column; EF just starts tracking it)

If the generated migration is missing the `vault_enabled` column, Task 1 was not built/discoverable when this command ran — rerun after confirming Task 1 compiles.

- [ ] **Step 3: Build to verify migrations compile**

Run: `dotnet build`
Expected: builds cleanly, including the new migration files.

- [ ] **Step 4: Commit**

```bash
git add src/Maxkeys.Infrastructure/Persistence/Configurations/KeyConfiguration.cs src/Maxkeys.Infrastructure/Persistence/Migrations/
git commit -m "feat(vault): add xmin concurrency token to Key, migration for vault_enabled + xmin"
```

---

### Task 3: `LoadVaultKeys` use case — bulk key load

**Files:**
- Create: `src/Maxkeys.Application/Fulfillment/LoadVaultKeys.cs`
- Test: `tests/Maxkeys.Application.Tests/Fulfillment/LoadVaultKeysTests.cs`

**Interfaces:**
- Consumes: `IAppDbContext` (`Products`, `ProductVariants`, `Keys`, `SaveChangesAsync`), `KeyCipher.Encrypt(string) -> (byte[] Blob, short Version)` (`src/Maxkeys.Application/Security/KeyCipher.cs`), `Key(Guid productVariantId, byte[] encryptedCode, short keyVersion, string loadedBy, DateTimeOffset now)` (`src/Maxkeys.Domain/Keys/Key.cs`).
- Produces: `LoadVaultKeys.ExecuteAsync(Guid productVariantId, IReadOnlyList<string> codes, string loadedBy, CancellationToken) -> Task<LoadVaultKeysResult?>`, `LoadVaultKeysResult(int AddedCount, int AvailableCount)` — consumed by Task 7's `AdminVaultEndpoints`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Maxkeys.Application.Tests/Fulfillment/LoadVaultKeysTests.cs`:

```csharp
using Maxkeys.Application.Fulfillment;
using Maxkeys.Application.Security;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Catalog;
using Maxkeys.Domain.Common;
using Maxkeys.Domain.Keys;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Tests.Fulfillment;

/// <summary>Covers <see cref="LoadVaultKeys"/> (vault spec: Bulk Key Load).</summary>
[Collection(PostgresCollection.Name)]
public sealed class LoadVaultKeysTests
{
    private static readonly string ValidKeyBase64 =
        Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private readonly PostgresFixture _fixture;

    public LoadVaultKeysTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Loading_codes_creates_available_keys_for_the_variant()
    {
        var variantId = await SeedVariantAsync();

        await using var context = _fixture.CreateContext();
        var sut = CreateSut(context);

        var result = await sut.ExecuteAsync(variantId, ["CODE-1", "CODE-2", "CODE-3"], "admin@maxkeys.test");

        Assert.NotNull(result);
        Assert.Equal(3, result!.AddedCount);
        Assert.Equal(3, result.AvailableCount);

        var keys = await context.Keys.Where(k => k.ProductVariantId == variantId).ToListAsync();
        Assert.Equal(3, keys.Count);
        Assert.All(keys, k => Assert.Equal(KeyStatus.Available, k.Status));
        Assert.All(keys, k => Assert.Equal("admin@maxkeys.test", k.LoadedBy));
    }

    [Fact]
    public async Task Loading_more_codes_adds_to_existing_available_count()
    {
        var variantId = await SeedVariantAsync();

        await using (var firstContext = _fixture.CreateContext())
        {
            await CreateSut(firstContext).ExecuteAsync(variantId, ["CODE-1"], "admin@maxkeys.test");
        }

        await using var context = _fixture.CreateContext();
        var result = await CreateSut(context).ExecuteAsync(variantId, ["CODE-2", "CODE-3"], "admin@maxkeys.test");

        Assert.NotNull(result);
        Assert.Equal(2, result!.AddedCount);
        Assert.Equal(3, result.AvailableCount);
    }

    [Fact]
    public async Task Empty_code_list_throws()
    {
        var variantId = await SeedVariantAsync();

        await using var context = _fixture.CreateContext();
        var sut = CreateSut(context);

        await Assert.ThrowsAsync<DomainException>(() => sut.ExecuteAsync(variantId, [], "admin@maxkeys.test"));
    }

    [Fact]
    public async Task Blank_code_in_the_list_throws()
    {
        var variantId = await SeedVariantAsync();

        await using var context = _fixture.CreateContext();
        var sut = CreateSut(context);

        await Assert.ThrowsAsync<DomainException>(() => sut.ExecuteAsync(variantId, ["CODE-1", "   "], "admin@maxkeys.test"));
    }

    [Fact]
    public async Task Unknown_variant_returns_null()
    {
        await using var context = _fixture.CreateContext();
        var sut = CreateSut(context);

        var result = await sut.ExecuteAsync(Guid.NewGuid(), ["CODE-1"], "admin@maxkeys.test");

        Assert.Null(result);
    }

    private static LoadVaultKeys CreateSut(AppDbContext context) =>
        new(context, new KeyCipher(Options.Create(new KeyCipherOptions { EncryptionKey = ValidKeyBase64, CurrentVersion = 1 })));

    private async Task<Guid> SeedVariantAsync()
    {
        await using var context = _fixture.CreateContext();
        var product = new Product($"p-{Guid.NewGuid():N}", "Product", $"platform-{Guid.NewGuid():N}");
        context.Products.Add(product);
        var variant = new ProductVariant(product.Id, 100m, "ARS", region: "AR", edition: "Standard");
        context.ProductVariants.Add(variant);
        await context.SaveChangesAsync();
        return variant.Id;
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Maxkeys.Application.Tests --filter "FullyQualifiedName~LoadVaultKeysTests"`
Expected: FAIL — `LoadVaultKeys` does not exist.

- [ ] **Step 3: Implement**

Create `src/Maxkeys.Application/Fulfillment/LoadVaultKeys.cs`:

```csharp
using Maxkeys.Application.Persistence;
using Maxkeys.Application.Security;
using Maxkeys.Domain.Common;
using Maxkeys.Domain.Keys;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Fulfillment;

/// <summary>
/// Bulk-loads vault key stock for a product variant (vault spec: Bulk Key
/// Load). Same encryption path as <see cref="AttachKeyToOrderItem"/> — each
/// code is encrypted independently and inserted as an
/// <see cref="KeyStatus.Available"/> <see cref="Key"/> row. Returns
/// <see langword="null"/> for an unknown variant so the API layer can map it
/// to 404.
/// </summary>
public sealed class LoadVaultKeys
{
    private readonly IAppDbContext _db;
    private readonly KeyCipher _keyCipher;

    public LoadVaultKeys(IAppDbContext db, KeyCipher keyCipher)
    {
        _db = db;
        _keyCipher = keyCipher;
    }

    public async Task<LoadVaultKeysResult?> ExecuteAsync(
        Guid productVariantId, IReadOnlyList<string> codes, string loadedBy, CancellationToken cancellationToken = default)
    {
        if (codes is null || codes.Count == 0)
        {
            throw new DomainException("At least one key code must be provided.");
        }

        if (codes.Any(string.IsNullOrWhiteSpace))
        {
            throw new DomainException("Key codes must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(loadedBy))
        {
            throw new DomainException("Loaded-by identity must not be empty.");
        }

        var variantExists = await _db.ProductVariants.AnyAsync(v => v.Id == productVariantId, cancellationToken);
        if (!variantExists)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var code in codes)
        {
            var (blob, version) = _keyCipher.Encrypt(code);
            _db.Keys.Add(new Key(productVariantId, blob, version, loadedBy, now));
        }

        await _db.SaveChangesAsync(cancellationToken);

        var availableCount = await _db.Keys.CountAsync(
            k => k.ProductVariantId == productVariantId && k.Status == KeyStatus.Available, cancellationToken);

        return new LoadVaultKeysResult(codes.Count, availableCount);
    }
}

/// <summary>Result of a bulk vault key load (vault spec: Bulk Key Load response shape).</summary>
public sealed record LoadVaultKeysResult(int AddedCount, int AvailableCount);
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Maxkeys.Application.Tests --filter "FullyQualifiedName~LoadVaultKeysTests"`
Expected: PASS (all 6 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Maxkeys.Application/Fulfillment/LoadVaultKeys.cs tests/Maxkeys.Application.Tests/Fulfillment/LoadVaultKeysTests.cs
git commit -m "feat(vault): add LoadVaultKeys use case for bulk key loading"
```

---

### Task 4: `ToggleProductVault` use case

**Files:**
- Create: `src/Maxkeys.Application/Fulfillment/ToggleProductVault.cs`
- Test: `tests/Maxkeys.Application.Tests/Fulfillment/ToggleProductVaultTests.cs`

**Interfaces:**
- Consumes: `IAppDbContext.Products`, `Product.SetVaultEnabled(bool)` (Task 1), `Product.VaultEnabled` (Task 1).
- Produces: `ToggleProductVault.ExecuteAsync(Guid productId, bool enabled, CancellationToken) -> Task<bool?>` — consumed by Task 7's `AdminVaultEndpoints`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Maxkeys.Application.Tests/Fulfillment/ToggleProductVaultTests.cs`:

```csharp
using Maxkeys.Application.Fulfillment;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Tests.Fulfillment;

/// <summary>Covers <see cref="ToggleProductVault"/> (vault spec: Per-Product Vault Toggle).</summary>
[Collection(PostgresCollection.Name)]
public sealed class ToggleProductVaultTests
{
    private readonly PostgresFixture _fixture;

    public ToggleProductVaultTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Enabling_the_vault_persists_the_flag()
    {
        var productId = await SeedProductAsync();

        await using var context = _fixture.CreateContext();
        var sut = new ToggleProductVault(context);

        var result = await sut.ExecuteAsync(productId, true);

        Assert.True(result);

        await using var readContext = _fixture.CreateContext();
        var product = await readContext.Products.SingleAsync(p => p.Id == productId);
        Assert.True(product.VaultEnabled);
    }

    [Fact]
    public async Task Disabling_an_enabled_vault_persists_the_flag()
    {
        var productId = await SeedProductAsync();

        await using (var firstContext = _fixture.CreateContext())
        {
            await new ToggleProductVault(firstContext).ExecuteAsync(productId, true);
        }

        await using var context = _fixture.CreateContext();
        var result = await new ToggleProductVault(context).ExecuteAsync(productId, false);

        Assert.False(result);
    }

    [Fact]
    public async Task Unknown_product_returns_null()
    {
        await using var context = _fixture.CreateContext();
        var sut = new ToggleProductVault(context);

        var result = await sut.ExecuteAsync(Guid.NewGuid(), true);

        Assert.Null(result);
    }

    private async Task<Guid> SeedProductAsync()
    {
        await using var context = _fixture.CreateContext();
        var product = new Product($"p-{Guid.NewGuid():N}", "Product", $"platform-{Guid.NewGuid():N}");
        context.Products.Add(product);
        await context.SaveChangesAsync();
        return product.Id;
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Maxkeys.Application.Tests --filter "FullyQualifiedName~ToggleProductVaultTests"`
Expected: FAIL — `ToggleProductVault` does not exist.

- [ ] **Step 3: Implement**

Create `src/Maxkeys.Application/Fulfillment/ToggleProductVault.cs`:

```csharp
using Maxkeys.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Fulfillment;

/// <summary>
/// Enables or disables vault auto-fulfillment for a product (vault spec:
/// Per-Product Vault Toggle) via <see cref="Domain.Catalog.Product.SetVaultEnabled"/>.
/// Returns <see langword="null"/> for an unknown product so the endpoint can
/// map it to 404.
/// </summary>
public sealed class ToggleProductVault
{
    private readonly IAppDbContext _db;

    public ToggleProductVault(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<bool?> ExecuteAsync(Guid productId, bool enabled, CancellationToken cancellationToken = default)
    {
        var product = await _db.Products.SingleOrDefaultAsync(p => p.Id == productId, cancellationToken);
        if (product is null)
        {
            return null;
        }

        product.SetVaultEnabled(enabled);
        await _db.SaveChangesAsync(cancellationToken);

        return product.VaultEnabled;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Maxkeys.Application.Tests --filter "FullyQualifiedName~ToggleProductVaultTests"`
Expected: PASS (all 3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Maxkeys.Application/Fulfillment/ToggleProductVault.cs tests/Maxkeys.Application.Tests/Fulfillment/ToggleProductVaultTests.cs
git commit -m "feat(vault): add ToggleProductVault use case"
```

---

### Task 5: `ListVaultStock` use case — admin stock listing

**Files:**
- Create: `src/Maxkeys.Application/Fulfillment/ListVaultStock.cs`
- Test: `tests/Maxkeys.Application.Tests/Fulfillment/ListVaultStockTests.cs`

**Interfaces:**
- Consumes: `IAppDbContext.Products`, `.ProductVariants`, `.Keys`; `Product.VaultEnabled` (Task 1).
- Produces: `ListVaultStock.ExecuteAsync(CancellationToken) -> Task<IReadOnlyList<VaultProduct>>`, `VaultProduct(Guid Id, string Name, bool VaultEnabled, IReadOnlyList<VaultVariant> Variants)`, `VaultVariant(Guid Id, string? Region, string? Edition, int AvailableCount, int AssignedCount)` — consumed by Task 7's `AdminVaultEndpoints`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Maxkeys.Application.Tests/Fulfillment/ListVaultStockTests.cs`:

```csharp
using Maxkeys.Application.Fulfillment;
using Maxkeys.Application.Security;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Catalog;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Tests.Fulfillment;

/// <summary>Covers <see cref="ListVaultStock"/> (vault spec: Vault Stock Listing).</summary>
[Collection(PostgresCollection.Name)]
public sealed class ListVaultStockTests
{
    private static readonly string ValidKeyBase64 =
        Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private readonly PostgresFixture _fixture;

    public ListVaultStockTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Lists_product_vault_flag_and_per_variant_key_counts()
    {
        Guid productId, variantId;
        await using (var seedContext = _fixture.CreateContext())
        {
            var product = new Product($"p-{Guid.NewGuid():N}", "Vault Product", $"platform-{Guid.NewGuid():N}");
            product.SetVaultEnabled(true);
            seedContext.Products.Add(product);
            var variant = new ProductVariant(product.Id, 100m, "ARS", region: "AR", edition: "Standard");
            seedContext.ProductVariants.Add(variant);
            await seedContext.SaveChangesAsync();
            productId = product.Id;
            variantId = variant.Id;
        }

        await using (var loadContext = _fixture.CreateContext())
        {
            var cipher = new KeyCipher(Options.Create(new KeyCipherOptions { EncryptionKey = ValidKeyBase64, CurrentVersion = 1 }));
            await new LoadVaultKeys(loadContext, cipher).ExecuteAsync(variantId, ["CODE-1", "CODE-2"], "admin@maxkeys.test");
        }

        await using var context = _fixture.CreateContext();
        var sut = new ListVaultStock(context);

        var products = await sut.ExecuteAsync();

        var vaultProduct = Assert.Single(products, p => p.Id == productId);
        Assert.True(vaultProduct.VaultEnabled);
        var variantStock = Assert.Single(vaultProduct.Variants, v => v.Id == variantId);
        Assert.Equal(2, variantStock.AvailableCount);
        Assert.Equal(0, variantStock.AssignedCount);
    }

    [Fact]
    public async Task Product_with_no_variants_has_an_empty_variant_list()
    {
        Guid productId;
        await using (var seedContext = _fixture.CreateContext())
        {
            var product = new Product($"p-{Guid.NewGuid():N}", "Empty Product", $"platform-{Guid.NewGuid():N}");
            seedContext.Products.Add(product);
            await seedContext.SaveChangesAsync();
            productId = product.Id;
        }

        await using var context = _fixture.CreateContext();
        var sut = new ListVaultStock(context);

        var products = await sut.ExecuteAsync();

        var listed = Assert.Single(products, p => p.Id == productId);
        Assert.False(listed.VaultEnabled);
        Assert.Empty(listed.Variants);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Maxkeys.Application.Tests --filter "FullyQualifiedName~ListVaultStockTests"`
Expected: FAIL — `ListVaultStock` does not exist.

- [ ] **Step 3: Implement**

Create `src/Maxkeys.Application/Fulfillment/ListVaultStock.cs`:

```csharp
using Maxkeys.Application.Persistence;
using Maxkeys.Domain.Keys;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Fulfillment;

/// <summary>
/// Lists every product's vault state for the admin vault view (vault spec:
/// Vault Stock Listing) — per product, whether auto-fulfillment is enabled;
/// per variant, available/assigned key counts. Ordered by product name, same
/// products-then-variants shape as <c>ListAdminProducts</c>.
/// </summary>
public sealed class ListVaultStock
{
    private readonly IAppDbContext _db;

    public ListVaultStock(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<VaultProduct>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var products = await _db.Products
            .OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Name, p.VaultEnabled })
            .ToListAsync(cancellationToken);

        var productIds = products.Select(p => p.Id).ToList();

        var variants = await _db.ProductVariants
            .Where(v => productIds.Contains(v.ProductId))
            .OrderBy(v => v.SortOrder)
            .ToListAsync(cancellationToken);

        var variantIds = variants.Select(v => v.Id).ToList();

        var keyCounts = await _db.Keys
            .Where(k => variantIds.Contains(k.ProductVariantId))
            .GroupBy(k => new { k.ProductVariantId, k.Status })
            .Select(g => new KeyCountRow(g.Key.ProductVariantId, g.Key.Status, g.Count()))
            .ToListAsync(cancellationToken);

        var variantsByProduct = variants.GroupBy(v => v.ProductId).ToDictionary(g => g.Key, g => g.ToList());

        return products.Select(p => new VaultProduct(
            p.Id,
            p.Name,
            p.VaultEnabled,
            (variantsByProduct.TryGetValue(p.Id, out var productVariants) ? productVariants : [])
                .Select(v => new VaultVariant(
                    v.Id,
                    v.Region,
                    v.Edition,
                    CountFor(keyCounts, v.Id, KeyStatus.Available),
                    CountFor(keyCounts, v.Id, KeyStatus.Assigned)))
                .ToList()))
            .ToList();
    }

    private static int CountFor(IReadOnlyList<KeyCountRow> keyCounts, Guid variantId, KeyStatus status) =>
        keyCounts.FirstOrDefault(c => c.ProductVariantId == variantId && c.Status == status)?.Count ?? 0;

    private sealed record KeyCountRow(Guid ProductVariantId, KeyStatus Status, int Count);
}

/// <summary>One variant's vault stock (vault spec: Vault Stock Listing response shape).</summary>
public sealed record VaultVariant(Guid Id, string? Region, string? Edition, int AvailableCount, int AssignedCount);

/// <summary>One product's vault state, including every variant's stock (vault spec: Vault Stock Listing response shape).</summary>
public sealed record VaultProduct(Guid Id, string Name, bool VaultEnabled, IReadOnlyList<VaultVariant> Variants);
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Maxkeys.Application.Tests --filter "FullyQualifiedName~ListVaultStockTests"`
Expected: PASS (both tests).

- [ ] **Step 5: Commit**

```bash
git add src/Maxkeys.Application/Fulfillment/ListVaultStock.cs tests/Maxkeys.Application.Tests/Fulfillment/ListVaultStockTests.cs
git commit -m "feat(vault): add ListVaultStock use case for admin stock view"
```

---

### Task 6: Auto-assign vault keys on payment approval

**Files:**
- Modify: `src/Maxkeys.Application/Outbox/OrderApprovedHandler.cs`
- Test: `tests/Maxkeys.Application.Tests/Outbox/OrderApprovedHandlerTests.cs`

**Interfaces:**
- Consumes: `Order.AttachKey(Guid orderItemId, Key key, DateTimeOffset now) -> bool` (`src/Maxkeys.Domain/Orders/Order.cs`, unchanged), `Product.VaultEnabled` (Task 1), `Key.CreatedAt`/`Status` (`src/Maxkeys.Domain/Keys/Key.cs`, unchanged), the `xmin` concurrency token from Task 2.
- Produces: no new public surface — `OrderApprovedHandler.HandleAsync` behavior changes (existing signature, `IOutboxHandler.HandleAsync(OutboxEvent, CancellationToken) -> Task`, unchanged).

This is the core integration point. This task extends the **existing** test file rather than creating a new one — read the current file first; the tests below assume the existing three tests (`Paid_order_transitions_to_awaiting_fulfillment_and_sends_one_operator_email`, `Already_awaiting_fulfillment_is_a_noop_with_no_email`, `Unknown_order_throws`) stay unchanged in place.

- [ ] **Step 1: Write the failing tests**

Add to `tests/Maxkeys.Application.Tests/Outbox/OrderApprovedHandlerTests.cs`. First, add these usings at the top (alongside the existing ones):

```csharp
using Maxkeys.Application.Fulfillment;
using Maxkeys.Application.Security;
using Maxkeys.Domain.Catalog;
using Maxkeys.Domain.Keys;
```

Then add these test methods inside the `OrderApprovedHandlerTests` class (after `Unknown_order_throws`):

```csharp
    [Fact]
    public async Task Vault_enabled_with_full_stock_auto_delivers_and_skips_the_operator_email()
    {
        var (orderId, variantId) = await SeedPaidOrderWithVaultProductAsync(quantity: 2, vaultEnabled: true);
        await LoadVaultKeysAsync(variantId, "CODE-1", "CODE-2");

        var emailSender = new RecordingEmailSender();
        await using var context = _fixture.CreateContext();
        var sut = CreateHandler(context, emailSender);

        await sut.HandleAsync(OrderApprovedEvent(orderId), CancellationToken.None);

        var order = await context.Orders.Include(o => o.Items).ThenInclude(i => i.Keys).SingleAsync(o => o.Id == orderId);
        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.NotNull(order.DeliveredAt);
        Assert.All(order.Items.Single().Keys, k => Assert.Equal(KeyStatus.Assigned, k.Status));
        Assert.Empty(emailSender.SentMessages);

        var deliveredEvents = await context.OutboxEvents.Where(e => e.Type == OutboxEventTypes.OrderDelivered).ToListAsync();
        Assert.Single(deliveredEvents, e => e.Payload.Contains(orderId.ToString()));
    }

    [Fact]
    public async Task Vault_enabled_with_partial_stock_skips_that_item_and_still_sends_the_operator_email()
    {
        var (orderId, variantId) = await SeedPaidOrderWithVaultProductAsync(quantity: 3, vaultEnabled: true);
        await LoadVaultKeysAsync(variantId, "CODE-1", "CODE-2"); // only 2 of the 3 needed

        var emailSender = new RecordingEmailSender();
        await using var context = _fixture.CreateContext();
        var sut = CreateHandler(context, emailSender);

        await sut.HandleAsync(OrderApprovedEvent(orderId), CancellationToken.None);

        var order = await context.Orders.Include(o => o.Items).ThenInclude(i => i.Keys).SingleAsync(o => o.Id == orderId);
        Assert.Equal(OrderStatus.AwaitingFulfillment, order.Status);
        Assert.Empty(order.Items.Single().Keys); // all-or-nothing: zero keys assigned, not 2 of 3
        Assert.Single(emailSender.SentMessages);

        var availableRemaining = await context.Keys.CountAsync(k => k.ProductVariantId == variantId && k.Status == KeyStatus.Available);
        Assert.Equal(2, availableRemaining); // stock untouched
    }

    [Fact]
    public async Task Vault_disabled_never_auto_assigns_even_with_stock_available()
    {
        var (orderId, variantId) = await SeedPaidOrderWithVaultProductAsync(quantity: 1, vaultEnabled: false);
        await LoadVaultKeysAsync(variantId, "CODE-1");

        var emailSender = new RecordingEmailSender();
        await using var context = _fixture.CreateContext();
        var sut = CreateHandler(context, emailSender);

        await sut.HandleAsync(OrderApprovedEvent(orderId), CancellationToken.None);

        var order = await context.Orders.Include(o => o.Items).ThenInclude(i => i.Keys).SingleAsync(o => o.Id == orderId);
        Assert.Equal(OrderStatus.AwaitingFulfillment, order.Status);
        Assert.Empty(order.Items.Single().Keys);
        Assert.Single(emailSender.SentMessages);
    }

    private async Task<(Guid OrderId, Guid VariantId)> SeedPaidOrderWithVaultProductAsync(int quantity, bool vaultEnabled)
    {
        Guid variantId;
        await using (var context = _fixture.CreateContext())
        {
            var product = new Product($"p-{Guid.NewGuid():N}", "Vault Product", $"platform-{Guid.NewGuid():N}");
            product.SetVaultEnabled(vaultEnabled);
            context.Products.Add(product);
            var variant = new ProductVariant(product.Id, 1_000m, "ARS", region: "AR", edition: "Standard");
            context.ProductVariants.Add(variant);
            await context.SaveChangesAsync();
            variantId = variant.Id;
        }

        var now = DateTimeOffset.UtcNow;
        var order = Order.Create(
            null,
            $"buyer-{Guid.NewGuid():N}@example.com",
            [new OrderLine(variantId, "Vault Product", "Standard", 1_000m, quantity)],
            now);
        order.MarkPaid($"pay-{Guid.NewGuid():N}", now);

        await using (var context = _fixture.CreateContext())
        {
            context.Orders.Add(order);
            await context.SaveChangesAsync();
        }

        return (order.Id, variantId);
    }

    private async Task LoadVaultKeysAsync(Guid variantId, params string[] codes)
    {
        await using var context = _fixture.CreateContext();
        var cipher = new KeyCipher(Options.Create(new KeyCipherOptions
        {
            EncryptionKey = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()),
            CurrentVersion = 1,
        }));
        await new LoadVaultKeys(context, cipher).ExecuteAsync(variantId, codes, "seed-admin");
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Maxkeys.Application.Tests --filter "FullyQualifiedName~OrderApprovedHandlerTests"`
Expected: the 3 pre-existing tests still pass; the 3 new tests FAIL (no auto-assignment happens yet — vault-enabled orders stay `AwaitingFulfillment` and always get an email).

- [ ] **Step 3: Implement**

Replace `src/Maxkeys.Application/Outbox/OrderApprovedHandler.cs` in full:

```csharp
using System.Text.Json;
using Maxkeys.Application.Notifications;
using Maxkeys.Application.Persistence;
using Maxkeys.Domain.Keys;
using Maxkeys.Domain.Orders;
using Maxkeys.Domain.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Outbox;

/// <summary>
/// Handles <see cref="OutboxEventTypes.OrderApproved"/> (outbox-processing
/// spec: OrderApproved Handler; design section 6c; vault spec: Vault
/// Auto-Assignment On Approval). Transitions the order
/// <see cref="OrderStatus.Paid"/> → <see cref="OrderStatus.AwaitingFulfillment"/>,
/// then attempts to auto-assign vault stock to every item whose product has
/// vault auto-fulfillment enabled (all-or-nothing per item — see
/// <see cref="AutoAssignVaultKeysAsync"/>). Sends the operator notification
/// only if the order is not fully <see cref="OrderStatus.Delivered"/>
/// afterwards. Idempotent: an order already past <see cref="OrderStatus.Paid"/>
/// is a logged no-op with no email; an order that does not exist is a bug in
/// the producer, so this throws to let the outbox processor retry/dead-letter
/// rather than silently drop the event.
/// </summary>
public sealed class OrderApprovedHandler : IOutboxHandler
{
    private readonly IAppDbContext _db;
    private readonly IEmailSender _emailSender;
    private readonly IOptions<EmailOptions> _emailOptions;
    private readonly ILogger<OrderApprovedHandler> _logger;

    public string EventType => OutboxEventTypes.OrderApproved;

    public OrderApprovedHandler(
        IAppDbContext db,
        IEmailSender emailSender,
        IOptions<EmailOptions> emailOptions,
        ILogger<OrderApprovedHandler> logger)
    {
        _db = db;
        _emailSender = emailSender;
        _emailOptions = emailOptions;
        _logger = logger;
    }

    public async Task HandleAsync(OutboxEvent evt, CancellationToken cancellationToken)
    {
        var orderId = ParseOrderId(evt.Payload);
        var now = DateTimeOffset.UtcNow;

        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order is null)
        {
            throw new InvalidOperationException($"OrderApproved event references unknown order {orderId}.");
        }

        if (order.Status is OrderStatus.AwaitingFulfillment or OrderStatus.Delivered)
        {
            _logger.LogInformation(
                "Order {OrderId} is already {Status}; OrderApproved handling is a no-op.", orderId, order.Status);
            return;
        }

        try
        {
            await ApproveAsync(order, now, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            ClearTracker();

            // Same reload-and-retry-once pattern as AttachKeyToOrderItem: a concurrent auto-assign
            // for another order may have consumed the same vault keys between our read and write.
            order = await LoadOrderAsync(orderId, cancellationToken)
                ?? throw new InvalidOperationException($"OrderApproved event references unknown order {orderId}.");

            if (order.Status is OrderStatus.AwaitingFulfillment or OrderStatus.Delivered)
            {
                _logger.LogInformation(
                    "Order {OrderId} is already {Status} after a concurrent update; OrderApproved handling is a no-op.",
                    orderId, order.Status);
                return;
            }

            await ApproveAsync(order, now, cancellationToken);
        }

        if (order.Status != OrderStatus.Delivered)
        {
            var (subject, textBody) = EmailTemplates.OperatorOrderAwaitingFulfillment(order);
            await _emailSender.SendAsync(new EmailMessage(_emailOptions.Value.OperatorTo, subject, textBody), cancellationToken);
        }
    }

    private async Task ApproveAsync(Order order, DateTimeOffset now, CancellationToken cancellationToken)
    {
        order.MarkAwaitingFulfillment(now);
        await AutoAssignVaultKeysAsync(order, now, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Attempts to auto-assign vault stock to every incomplete item whose
    /// product has <see cref="Domain.Catalog.Product.VaultEnabled"/> set
    /// (vault spec: Vault Auto-Assignment On Approval). Per item, all-or-nothing:
    /// if available stock is less than the item's remaining quantity, that item
    /// is skipped entirely and left for the existing manual flow. Reuses
    /// <see cref="Order.AttachKey"/> unchanged, so the same completeness/status
    /// derivation applies. Inserts the <see cref="OutboxEventTypes.OrderDelivered"/>
    /// row itself if this pass completes the order — <see cref="Order.AttachKey"/>
    /// only returns whether it did, it does not touch the outbox.
    /// </summary>
    private async Task AutoAssignVaultKeysAsync(Order order, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var incompleteItems = order.Items.Where(i => !i.IsComplete).ToList();
        if (incompleteItems.Count == 0)
        {
            return;
        }

        var variantIds = incompleteItems.Select(i => i.ProductVariantId).Distinct().ToList();
        var variantProductIds = await _db.ProductVariants
            .Where(v => variantIds.Contains(v.Id))
            .Select(v => new { v.Id, v.ProductId })
            .ToListAsync(cancellationToken);
        var productIdByVariant = variantProductIds.ToDictionary(v => v.Id, v => v.ProductId);

        var productIds = productIdByVariant.Values.Distinct().ToList();
        var vaultEnabledByProduct = await _db.Products
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.VaultEnabled })
            .ToDictionaryAsync(p => p.Id, p => p.VaultEnabled, cancellationToken);

        foreach (var item in incompleteItems)
        {
            if (!productIdByVariant.TryGetValue(item.ProductVariantId, out var productId) ||
                !vaultEnabledByProduct.TryGetValue(productId, out var vaultEnabled) ||
                !vaultEnabled)
            {
                continue;
            }

            var remaining = item.Quantity - item.Keys.Count(k => k.Status == KeyStatus.Assigned);
            if (remaining <= 0)
            {
                continue;
            }

            var availableKeys = await _db.Keys
                .Where(k => k.ProductVariantId == item.ProductVariantId && k.Status == KeyStatus.Available)
                .OrderBy(k => k.CreatedAt)
                .Take(remaining)
                .ToListAsync(cancellationToken);

            if (availableKeys.Count < remaining)
            {
                continue; // all-or-nothing: leave this item for the manual flow
            }

            foreach (var key in availableKeys)
            {
                order.AttachKey(item.Id, key, now);
            }
        }

        if (order.Status == OrderStatus.Delivered)
        {
            _db.OutboxEvents.Add(new OutboxEvent(OutboxEventTypes.OrderDelivered, $$"""{"orderId":"{{order.Id}}"}""", now));
        }
    }

    private Task<Order?> LoadOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
        _db.Orders
            .Include(o => o.Items).ThenInclude(i => i.Keys)
            .SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);

    private void ClearTracker()
    {
        if (_db is DbContext dbContext)
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    private static Guid ParseOrderId(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var orderIdText = document.RootElement.GetProperty("orderId").GetString();
        return Guid.Parse(orderIdText!);
    }
}
```

Note what changed vs. the original: `LoadOrderAsync` now includes `.ThenInclude(i => i.Keys)` (needed for `item.IsComplete`/`item.Keys.Count`), the transition+email is now retryable via `ApproveAsync`, and `AutoAssignVaultKeysAsync` runs between `MarkAwaitingFulfillment` and `SaveChangesAsync`. The 3 pre-existing tests seed orders with random, unseeded `ProductVariantId` GUIDs — `AutoAssignVaultKeysAsync`'s variant/product lookup returns nothing for those, so every item is skipped and behavior is byte-for-byte identical to before. No production code outside this file changes.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Maxkeys.Application.Tests --filter "FullyQualifiedName~OrderApprovedHandlerTests"`
Expected: PASS (all 6 tests — the 3 original plus the 3 new).

- [ ] **Step 5: Commit**

```bash
git add src/Maxkeys.Application/Outbox/OrderApprovedHandler.cs tests/Maxkeys.Application.Tests/Outbox/OrderApprovedHandlerTests.cs
git commit -m "feat(vault): auto-assign vault keys on payment approval"
```

---

### Task 7: Admin vault API + DI wiring

**Files:**
- Create: `src/Maxkeys.Api/Endpoints/AdminVaultEndpoints.cs`
- Modify: `src/Maxkeys.Api/Program.cs`
- Modify: `src/Maxkeys.Infrastructure/DependencyInjection.cs`
- Test: `tests/Maxkeys.Api.Tests/Admin/AdminVaultEndpointsTests.cs`

**Interfaces:**
- Consumes: `LoadVaultKeys` (Task 3), `ToggleProductVault` (Task 4), `ListVaultStock` (Task 5), `AdminPolicy.Name` (`src/Maxkeys.Api/Auth/AdminPolicy.cs`, existing).
- Produces: `GET /admin/vault/products`, `POST /admin/vault/variants/{id}/keys`, `PUT /admin/vault/products/{id}/toggle` — the contract the separate frontend repo (coordinated directly with peer session `maxkeys-front-2f`) will consume. No other task depends on this one.

- [ ] **Step 1: Write the failing tests**

Create `tests/Maxkeys.Api.Tests/Admin/AdminVaultEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Maxkeys.Api.Tests.Auth;
using Maxkeys.Application.Fulfillment;
using Maxkeys.Domain.Catalog;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Maxkeys.Api.Tests.Admin;

/// <summary>Covers the vault spec: Bulk Key Load, Per-Product Vault Toggle, Vault Stock Listing, Vault Admin Authorization.</summary>
[Collection(Hs256ApiCollection.Name)]
public sealed class AdminVaultEndpointsTests
{
    private const string NonAdminSub = "44444444-4444-4444-4444-444444444444";

    private readonly Hs256ApiTestFixture _factory;

    public AdminVaultEndpointsTests(Hs256ApiTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Anonymous_request_to_list_stock_is_rejected()
    {
        var response = await _factory.CreateClient().GetAsync("/admin/vault/products");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Non_admin_sub_is_forbidden_from_list_stock()
    {
        var response = await AdminClient(NonAdminSub).GetAsync("/admin/vault/products");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_list_vault_stock()
    {
        var (productId, variantId) = await SeedProductWithVariantAsync();

        var response = await AdminClient().GetAsync("/admin/vault/products");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var products = await response.Content.ReadFromJsonAsync<List<VaultProduct>>();
        var product = Assert.Single(products!, p => p.Id == productId);
        Assert.Single(product.Variants, v => v.Id == variantId);
    }

    [Fact]
    public async Task Admin_can_load_vault_keys_for_a_variant()
    {
        var (_, variantId) = await SeedProductWithVariantAsync();

        var response = await AdminClient().PostAsJsonAsync(
            $"/admin/vault/variants/{variantId}/keys", new { codes = new[] { "CODE-1", "CODE-2" } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<LoadVaultKeysResult>();
        Assert.Equal(2, result!.AddedCount);
        Assert.Equal(2, result.AvailableCount);
    }

    [Fact]
    public async Task Loading_keys_for_an_unknown_variant_returns_404()
    {
        var response = await AdminClient().PostAsJsonAsync(
            $"/admin/vault/variants/{Guid.NewGuid()}/keys", new { codes = new[] { "CODE-1" } });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_toggle_a_products_vault()
    {
        var (productId, _) = await SeedProductWithVariantAsync();

        var response = await AdminClient().PutAsJsonAsync(
            $"/admin/vault/products/{productId}/toggle", new { enabled = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var listResponse = await AdminClient().GetAsync("/admin/vault/products");
        var products = await listResponse.Content.ReadFromJsonAsync<List<VaultProduct>>();
        Assert.True(products!.Single(p => p.Id == productId).VaultEnabled);
    }

    [Fact]
    public async Task Toggling_an_unknown_product_returns_404()
    {
        var response = await AdminClient().PutAsJsonAsync(
            $"/admin/vault/products/{Guid.NewGuid()}/toggle", new { enabled = true });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<(Guid ProductId, Guid VariantId)> SeedProductWithVariantAsync()
    {
        Guid productId = default, variantId = default;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var product = new Product($"p-{Guid.NewGuid():N}", "Product", $"platform-{Guid.NewGuid():N}");
        db.Products.Add(product);
        var variant = new ProductVariant(product.Id, 100m, "ARS", region: "AR", edition: "Standard");
        db.ProductVariants.Add(variant);
        await db.SaveChangesAsync();
        productId = product.Id;
        variantId = variant.Id;
        return (productId, variantId);
    }

    private HttpClient AdminClient(string? sub = null)
    {
        var token = TestTokens.CreateHs256(
            sub ?? Hs256ApiTestFixture.AdminSub, Hs256ApiTestFixture.Issuer, Hs256ApiTestFixture.Audience, Hs256ApiTestFixture.Hs256Secret);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Maxkeys.Api.Tests --filter "FullyQualifiedName~AdminVaultEndpointsTests"`
Expected: FAIL — routes don't exist (404s/connection errors instead of the expected statuses), and `LoadVaultKeys`/`ToggleProductVault`/`ListVaultStock` aren't registered in DI even though they exist as classes.

- [ ] **Step 3: Implement**

Create `src/Maxkeys.Api/Endpoints/AdminVaultEndpoints.cs`:

```csharp
using System.Security.Claims;
using Maxkeys.Api.Auth;
using Maxkeys.Application.Fulfillment;

namespace Maxkeys.Api.Endpoints;

/// <summary>
/// Admin vault management endpoints (vault spec). Every route requires the
/// <see cref="AdminPolicy.Name"/> policy, same convention as
/// <see cref="AdminCatalogEndpoints"/>.
/// </summary>
public static class AdminVaultEndpoints
{
    public static IEndpointRouteBuilder MapAdminVaultEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/vault").RequireAuthorization(AdminPolicy.Name);

        group.MapGet("/products", async (ListVaultStock useCase, CancellationToken cancellationToken) =>
            Results.Ok(await useCase.ExecuteAsync(cancellationToken)));

        group.MapPost("/variants/{id:guid}/keys", async (
            Guid id,
            LoadVaultKeysRequest body,
            ClaimsPrincipal user,
            LoadVaultKeys useCase,
            CancellationToken cancellationToken) =>
        {
            var adminSub = user.FindFirst("sub")?.Value
                ?? throw new InvalidOperationException("Authenticated admin principal is missing a 'sub' claim.");

            var result = await useCase.ExecuteAsync(id, body.Codes, adminSub, cancellationToken);
            return result is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Product variant not found")
                : Results.Ok(result);
        });

        group.MapPut("/products/{id:guid}/toggle", async (
            Guid id,
            ToggleProductVaultRequest body,
            ToggleProductVault useCase,
            CancellationToken cancellationToken) =>
        {
            var enabled = await useCase.ExecuteAsync(id, body.Enabled, cancellationToken);
            return enabled is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Product not found")
                : Results.Ok(new ToggleProductVaultResponse(enabled.Value));
        });

        return app;
    }
}

/// <summary>Admin request body for <c>POST /admin/vault/variants/{id}/keys</c>.</summary>
public sealed record LoadVaultKeysRequest(IReadOnlyList<string> Codes);

/// <summary>Admin request body for <c>PUT /admin/vault/products/{id}/toggle</c>.</summary>
public sealed record ToggleProductVaultRequest(bool Enabled);

/// <summary>Response for <c>PUT /admin/vault/products/{id}/toggle</c>.</summary>
public sealed record ToggleProductVaultResponse(bool VaultEnabled);
```

In `src/Maxkeys.Api/Program.cs`, add the registration line after `app.MapAdminCatalogEndpoints();`:

```csharp
    app.MapAdminCatalogEndpoints();
    app.MapAdminVaultEndpoints();
```

In `src/Maxkeys.Infrastructure/DependencyInjection.cs`, add the three use cases to `AddUseCases` (after `services.AddScoped<AttachKeyToOrderItem>();`):

```csharp
        services.AddScoped<AttachKeyToOrderItem>();
        services.AddScoped<LoadVaultKeys>();
        services.AddScoped<ToggleProductVault>();
        services.AddScoped<ListVaultStock>();
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Maxkeys.Api.Tests --filter "FullyQualifiedName~AdminVaultEndpointsTests"`
Expected: PASS (all 7 tests).

- [ ] **Step 5: Run the full suite**

Run: `dotnet build && dotnet test`
Expected: everything passes — this is the final integration check across all 7 tasks.

- [ ] **Step 6: Commit**

```bash
git add src/Maxkeys.Api/Endpoints/AdminVaultEndpoints.cs src/Maxkeys.Api/Program.cs src/Maxkeys.Infrastructure/DependencyInjection.cs tests/Maxkeys.Api.Tests/Admin/AdminVaultEndpointsTests.cs
git commit -m "feat(vault): add admin vault API endpoints and DI wiring"
```

---

## After this plan

Hand the API contract to the frontend session (`maxkeys-front-2f`) directly:
- `GET /admin/vault/products` → `VaultProduct[]` (`{ id, name, vaultEnabled, variants: [{ id, region, edition, availableCount, assignedCount }] }`)
- `POST /admin/vault/variants/{id}/keys` body `{ codes: string[] }` → `LoadVaultKeysResult` (`{ addedCount, availableCount }`), 404 if variant unknown
- `PUT /admin/vault/products/{id}/toggle` body `{ enabled: bool }` → `ToggleProductVaultResponse` (`{ vaultEnabled }`), 404 if product unknown

No further backend work is implied by the frontend build unless the contract needs to change.
