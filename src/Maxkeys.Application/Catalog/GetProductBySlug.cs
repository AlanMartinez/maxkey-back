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

        var images = await _db.ProductImages
            .Where(i => i.ProductId == product.Id)
            .OrderBy(i => i.SortOrder)
            .ToListAsync(cancellationToken);

        var mainImageUrl = _imageUrlBuilder.Build(product.ImageKey);
        // Gallery carousel: catalog image first, then admin-uploaded gallery images in order
        // (admin-catalog spec). `ProductImages` never includes the main key, so it must be
        // prepended here or a product with any gallery images loses its catalog image entirely.
        var galleryUrls = new List<string>();
        if (!string.IsNullOrEmpty(mainImageUrl))
        {
            galleryUrls.Add(mainImageUrl);
        }
        galleryUrls.AddRange(images.Select(i => _imageUrlBuilder.Build(i.ImageKey)));

        return new ProductDetail(
            product.Id,
            product.Slug,
            product.Name,
            product.Platform,
            mainImageUrl,
            _imageUrlBuilder.Build(product.DetailImageKey),
            cheapest?.Price ?? 0m,
            cheapest is null ? null : VariantPricing.ComputeOldPrice(cheapest.Price, cheapest.DiscountPercentage),
            product.Description,
            variants.Select(ToVariantDetail).ToList(),
            galleryUrls,
            product.ActivationGuideUrl,
            product.ActivationType);
    }

    private static ProductVariantDetail ToVariantDetail(ProductVariant variant) =>
        new(
            variant.Id,
            BuildVariantName(variant),
            variant.Region,
            variant.Edition,
            variant.Price,
            VariantPricing.ComputeOldPrice(variant.Price, variant.DiscountPercentage),
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
