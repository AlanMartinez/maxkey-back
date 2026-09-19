using Maxkeys.Application.Persistence;
using Maxkeys.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Catalog;

/// <summary>
/// Lists every product for the admin catalog view, including inactive ones,
/// each with every variant regardless of <c>IsActive</c> (admin-catalog spec
/// "Admin Product Listing Including Inactive"; design D3). Ordered by name,
/// same shape as <see cref="GetCatalog"/> (products query, then a single
/// variants query keyed by product id). One class per use case (ADR-02).
/// </summary>
public sealed class ListAdminProducts
{
    private readonly IAppDbContext _db;
    private readonly ImageUrlBuilder _imageUrlBuilder;

    public ListAdminProducts(IAppDbContext db, ImageUrlBuilder imageUrlBuilder)
    {
        _db = db;
        _imageUrlBuilder = imageUrlBuilder;
    }

    public async Task<IReadOnlyList<AdminProduct>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var products = await _db.Products
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

        var productIds = products.Select(p => p.Id).ToList();

        var variants = await _db.ProductVariants
            .Where(v => productIds.Contains(v.ProductId))
            .ToListAsync(cancellationToken);

        var variantsByProduct = variants
            .GroupBy(v => v.ProductId)
            .ToDictionary(g => g.Key, g => g.OrderBy(v => v.SortOrder).ToList());

        var images = await _db.ProductImages
            .Where(i => productIds.Contains(i.ProductId))
            .ToListAsync(cancellationToken);

        var imagesByProduct = images
            .GroupBy(i => i.ProductId)
            .ToDictionary(g => g.Key, g => g.OrderBy(i => i.SortOrder).ToList());

        return products
            .Select(p => ToAdminProduct(
                p,
                variantsByProduct.TryGetValue(p.Id, out var productVariants) ? productVariants : [],
                imagesByProduct.TryGetValue(p.Id, out var productImages) ? productImages : [],
                _imageUrlBuilder))
            .ToList();
    }

    /// <summary>Shared admin product mapper, reused by every admin catalog use case (design decision).</summary>
    public static AdminProduct ToAdminProduct(
        Product product, IReadOnlyList<ProductVariant> variants, IReadOnlyList<ProductImage> images, ImageUrlBuilder imageUrlBuilder) =>
        new(
            product.Id,
            product.Slug,
            product.Name,
            product.Platform,
            product.IsActive,
            product.ImageKey,
            imageUrlBuilder.Build(product.ImageKey),
            product.DetailImageKey,
            imageUrlBuilder.Build(product.DetailImageKey),
            product.Description,
            variants.Select(ToAdminVariant).ToList(),
            images.Select(i => i.ImageKey).ToList(),
            images.Select(i => imageUrlBuilder.Build(i.ImageKey)).ToList(),
            product.ActivationGuideUrl,
            product.ActivationType);

    public static AdminVariant ToAdminVariant(ProductVariant variant) =>
        new(variant.Id, variant.Region, variant.Edition, variant.Price, VariantPricing.ComputeOldPrice(variant.Price, variant.DiscountPercentage), variant.Currency, variant.SortOrder, variant.IsActive, variant.IsRecommended);
}
