using Maxkeys.Application.Persistence;
using Maxkeys.Domain.Catalog;
using Maxkeys.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Catalog;

/// <summary>
/// Updates a product's admin-editable catalog fields via
/// <see cref="Domain.Catalog.Product.UpdateCatalogInfo"/> — the same domain
/// method the seeder uses (design D3: reuse, no new domain method; admin-catalog
/// spec "Admin Product Content Update", "Admin Product Activation Toggle").
/// <c>Slug</c> goes through the separate <see cref="Domain.Catalog.Product.RenameSlug"/>
/// instead, so <c>CatalogSeeder</c>'s upsert-by-slug path is unaffected. A slug
/// already taken by another product throws <see cref="DomainConflictException"/>
/// (mapped to 409), same as <see cref="CreateProduct"/>. Returns
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
        string slug,
        string name,
        string platform,
        string? description,
        string? imageKey,
        string? detailImageKey,
        bool isActive,
        IReadOnlyList<string>? imageKeys = null,
        Guid? activationGuideId = null,
        string? activationType = null,
        CancellationToken cancellationToken = default)
    {
        var product = await _db.Products.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
        {
            return null;
        }

        var slugTaken = await _db.Products.AnyAsync(p => p.Id != id && p.Slug == slug, cancellationToken);
        if (slugTaken)
        {
            throw new DomainConflictException($"A product with slug '{slug}' already exists.");
        }

        product.RenameSlug(slug);
        product.UpdateCatalogInfo(name, platform, description, imageKey, detailImageKey, isActive, activationGuideId, activationType);

        var existingImages = await _db.ProductImages.Where(i => i.ProductId == id).ToListAsync(cancellationToken);
        _db.ProductImages.RemoveRange(existingImages);
        var newImages = (imageKeys ?? [])
            .Select((key, index) => new ProductImage(id, key, index))
            .ToList();
        await _db.ProductImages.AddRangeAsync(newImages, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        var variants = await _db.ProductVariants
            .Where(v => v.ProductId == product.Id)
            .OrderBy(v => v.SortOrder)
            .ToListAsync(cancellationToken);

        return ListAdminProducts.ToAdminProduct(product, variants, newImages.OrderBy(i => i.SortOrder).ToList(), _imageUrlBuilder);
    }
}
