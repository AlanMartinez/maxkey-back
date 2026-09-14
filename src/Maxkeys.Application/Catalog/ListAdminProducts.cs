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

        return products
            .Select(p => new AdminProduct(
                p.Id,
                p.Slug,
                p.Name,
                p.Platform,
                p.IsActive,
                p.ImageKey,
                _imageUrlBuilder.Build(p.ImageKey),
                p.Description,
                variantsByProduct.TryGetValue(p.Id, out var productVariants)
                    ? productVariants.Select(ToAdminVariant).ToList()
                    : []))
            .ToList();
    }

    private static AdminVariant ToAdminVariant(ProductVariant variant) =>
        new(variant.Id, variant.Region, variant.Edition, variant.Price, variant.OldPrice, variant.Currency, variant.SortOrder, variant.IsActive);
}
