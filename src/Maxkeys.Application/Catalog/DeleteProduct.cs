using Maxkeys.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Catalog;

/// <summary>
/// Soft-deletes a product by flipping <c>IsActive</c> to <see langword="false"/>
/// via <see cref="Domain.Catalog.Product.UpdateCatalogInfo"/>, re-sending every
/// other field unchanged (admin-catalog spec "Admin Product Soft-Delete" and
/// "Uniform Soft-Delete Guarantee" — the row, its variants, gallery images and
/// order-history references are never hard-deleted, unlike
/// <see cref="DeleteProductVariant"/>). Returns <see langword="null"/> for an
/// unknown id so the endpoint can map it to 404, matching
/// <see cref="UpdateProduct"/>. One class per use case (ADR-02).
/// </summary>
public sealed class DeleteProduct
{
    private readonly IAppDbContext _db;
    private readonly ImageUrlBuilder _imageUrlBuilder;

    public DeleteProduct(IAppDbContext db, ImageUrlBuilder imageUrlBuilder)
    {
        _db = db;
        _imageUrlBuilder = imageUrlBuilder;
    }

    public async Task<AdminProduct?> ExecuteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await _db.Products.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
        {
            return null;
        }

        product.UpdateCatalogInfo(
            product.Name,
            product.Platform,
            product.Description,
            product.ImageKey,
            product.DetailImageKey,
            isActive: false,
            product.ActivationGuideUrl,
            product.ActivationType);
        await _db.SaveChangesAsync(cancellationToken);

        var variants = await _db.ProductVariants
            .Where(v => v.ProductId == product.Id)
            .OrderBy(v => v.SortOrder)
            .ToListAsync(cancellationToken);

        var images = await _db.ProductImages
            .Where(i => i.ProductId == product.Id)
            .OrderBy(i => i.SortOrder)
            .ToListAsync(cancellationToken);

        return ListAdminProducts.ToAdminProduct(product, variants, images, _imageUrlBuilder);
    }
}
