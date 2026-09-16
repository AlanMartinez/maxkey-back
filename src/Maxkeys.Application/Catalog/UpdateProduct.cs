using Maxkeys.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Catalog;

/// <summary>
/// Updates a product's admin-editable catalog fields via
/// <see cref="Domain.Catalog.Product.UpdateCatalogInfo"/> — the same domain
/// method the seeder uses (design D3: reuse, no new domain method; admin-catalog
/// spec "Admin Product Content Update", "Admin Product Activation Toggle").
/// <c>Slug</c> is never accepted, matching the domain method. Returns
/// <see langword="null"/> for an unknown id so the endpoint can map it to 404,
/// matching <see cref="GetProductBySlug"/>/<c>AttachKeyToOrderItem</c>. Domain
/// validation failures surface as <c>DomainException</c>, mapped to 422 by
/// <c>ProblemDetailsExceptionHandler</c> — the product stays unchanged because
/// the throw happens before <c>SaveChangesAsync</c>.
/// </summary>
public sealed class UpdateProduct
{
    private readonly IAppDbContext _db;
    private readonly ImageUrlBuilder _imageUrlBuilder;

    public UpdateProduct(IAppDbContext db, ImageUrlBuilder imageUrlBuilder)
    {
        _db = db;
        _imageUrlBuilder = imageUrlBuilder;
    }

    public async Task<AdminProduct?> ExecuteAsync(
        Guid id,
        string name,
        string platform,
        string? description,
        string? imageKey,
        string? detailImageKey,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var product = await _db.Products.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
        {
            return null;
        }

        product.UpdateCatalogInfo(name, platform, description, imageKey, detailImageKey, isActive);
        await _db.SaveChangesAsync(cancellationToken);

        var variants = await _db.ProductVariants
            .Where(v => v.ProductId == product.Id)
            .OrderBy(v => v.SortOrder)
            .ToListAsync(cancellationToken);

        return ListAdminProducts.ToAdminProduct(product, variants, _imageUrlBuilder);
    }
}
