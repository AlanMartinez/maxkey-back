using Maxkeys.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Catalog;

/// <summary>
/// Lists active products for the public catalog (catalog spec "Product
/// Listing"), optionally filtered by an exact <paramref name="platform"/>
/// match and a case-insensitive substring search on the product name. Results
/// are ordered by name; the card price comes from the recommended active
/// variant, falling back to the cheapest (<see cref="VariantPricing.PickDisplayVariant"/>).
/// One class per use case (ADR-02).
/// </summary>
public sealed class GetCatalog
{
    private readonly IAppDbContext _db;
    private readonly ImageUrlBuilder _imageUrlBuilder;

    public GetCatalog(IAppDbContext db, ImageUrlBuilder imageUrlBuilder)
    {
        _db = db;
        _imageUrlBuilder = imageUrlBuilder;
    }

    public async Task<IReadOnlyList<ProductSummary>> ExecuteAsync(
        string? platform,
        string? q,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Products.Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(platform))
        {
            query = query.Where(p => p.Platform == platform);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = q.ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(pattern));
        }

        var products = await query
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

        var productIds = products.Select(p => p.Id).ToList();

        var activeVariants = await _db.ProductVariants
            .Where(v => v.IsActive && productIds.Contains(v.ProductId))
            .ToListAsync(cancellationToken);

        var displayVariantByProduct = activeVariants
            .GroupBy(v => v.ProductId)
            .ToDictionary(g => g.Key, g => VariantPricing.PickDisplayVariant(g)!);

        return products
            .Select(p =>
            {
                displayVariantByProduct.TryGetValue(p.Id, out var displayVariant);
                return new ProductSummary(
                    p.Id,
                    p.Slug,
                    p.Name,
                    p.Platform,
                    _imageUrlBuilder.Build(p.ImageKey),
                    displayVariant?.Price ?? 0m,
                    displayVariant is null ? null : VariantPricing.ComputeOldPrice(displayVariant.Price, displayVariant.DiscountPercentage));
            })
            .ToList();
    }
}
