using Maxkeys.Application.Persistence;
using Maxkeys.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Catalog;

/// <summary>
/// Resolves a product by slug for the public product-detail page (catalog spec
/// "Product Detail Lookup"). Returns <see langword="null"/> for an unknown slug
/// or an inactive product — the API endpoint maps that to 404 (design section 7).
/// Only active variants are returned, ordered by <see cref="ProductVariant.SortOrder"/>.
/// One class per use case (ADR-02).
/// </summary>
public sealed class GetProductBySlug
{
    private readonly IAppDbContext _db;
    private readonly ImageUrlBuilder _imageUrlBuilder;

    public GetProductBySlug(IAppDbContext db, ImageUrlBuilder imageUrlBuilder)
    {
        _db = db;
        _imageUrlBuilder = imageUrlBuilder;
    }

    public async Task<ProductDetail?> ExecuteAsync(string slug, CancellationToken cancellationToken = default)
    {
        var product = await _db.Products
            .Where(p => p.IsActive && p.Slug == slug)
            .SingleOrDefaultAsync(cancellationToken);

        if (product is null)
        {
            return null;
        }

        var variants = await _db.ProductVariants
            .Where(v => v.IsActive && v.ProductId == product.Id)
            .OrderBy(v => v.SortOrder)
            .ToListAsync(cancellationToken);

        var cheapest = variants.OrderBy(v => v.Price).FirstOrDefault();

        return new ProductDetail(
            product.Id,
            product.Slug,
            product.Name,
            product.Platform,
            _imageUrlBuilder.Build(product.ImageKey),
            cheapest?.Price ?? 0m,
            cheapest?.OldPrice,
            product.Description,
            variants.Select(ToVariantDetail).ToList());
    }

    private static ProductVariantDetail ToVariantDetail(ProductVariant variant) =>
        new(
            variant.Id,
            BuildVariantName(variant),
            variant.Region,
            variant.Edition,
            variant.Price,
            variant.OldPrice,
            variant.Currency);

    /// <summary><see cref="ProductVariant"/> has no stored display name; compose one from region/edition.</summary>
    private static string BuildVariantName(ProductVariant variant)
    {
        if (!string.IsNullOrWhiteSpace(variant.Region) && !string.IsNullOrWhiteSpace(variant.Edition))
        {
            return $"{variant.Region} · {variant.Edition}";
        }

        return variant.Edition ?? variant.Region ?? "Estándar";
    }
}
