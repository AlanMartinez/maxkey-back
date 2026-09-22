using Maxkeys.Application.Catalog;
using Maxkeys.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Wishlist;

/// <summary>
/// Lists the caller's wishlist (wishlist spec). Excludes products that have
/// since become inactive (spec decision 3 — matches
/// <c>GetCatalog</c>'s <c>Products.Where(p => p.IsActive)</c>). Price/OldPrice
/// come from the product's current recommended active variant, falling back
/// to the cheapest active one, via the same
/// <see cref="VariantPricing.PickDisplayVariant"/> rule <c>GetCatalog</c> uses
/// — so a saved item's price always matches what the catalog card shows.
/// Ordered newest-added first.
/// </summary>
public sealed class GetMyWishlist
{
    private readonly IAppDbContext _db;
    private readonly ImageUrlBuilder _imageUrlBuilder;

    public GetMyWishlist(IAppDbContext db, ImageUrlBuilder imageUrlBuilder)
    {
        _db = db;
        _imageUrlBuilder = imageUrlBuilder;
    }

    public async Task<IReadOnlyList<WishlistItemSummary>> ExecuteAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var wishlistItems = await _db.WishlistItems
            .Where(w => w.UserId == userId)
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync(cancellationToken);

        var productIds = wishlistItems.Select(w => w.ProductId).ToList();

        var products = await _db.Products
            .Where(p => p.IsActive && productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var activeVariants = await _db.ProductVariants
            .Where(v => v.IsActive && productIds.Contains(v.ProductId))
            .ToListAsync(cancellationToken);

        var displayVariantByProduct = activeVariants
            .GroupBy(v => v.ProductId)
            .ToDictionary(g => g.Key, g => VariantPricing.PickDisplayVariant(g)!);

        var summaries = new List<WishlistItemSummary>();
        foreach (var item in wishlistItems)
        {
            if (!products.TryGetValue(item.ProductId, out var product))
            {
                continue;
            }

            displayVariantByProduct.TryGetValue(product.Id, out var displayVariant);
            summaries.Add(new WishlistItemSummary(
                product.Id,
                product.Slug,
                product.Name,
                product.Platform,
                _imageUrlBuilder.Build(product.ImageKey),
                displayVariant?.Price ?? 0m,
                displayVariant is null ? null : VariantPricing.ComputeOldPrice(displayVariant.Price, displayVariant.DiscountPercentage),
                item.CreatedAt));
        }

        return summaries;
    }
}

/// <summary>Wishlist list item (design section 7, <c>GET /me/wishlist</c> response item).</summary>
public sealed record WishlistItemSummary(
    Guid ProductId,
    string Slug,
    string Name,
    string Platform,
    string ImageUrl,
    decimal FromPrice,
    decimal? OldPrice,
    DateTimeOffset AddedAt);
