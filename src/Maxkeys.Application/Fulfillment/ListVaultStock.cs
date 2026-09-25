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
            .AsNoTracking()
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
