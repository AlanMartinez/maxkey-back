using Maxkeys.Application.Persistence;
using Maxkeys.Domain.Catalog;
using Maxkeys.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Catalog;

/// <summary>
/// Creates a new product from the admin catalog view via <see cref="Domain.Catalog.Product"/>'s
/// constructor (same invariants as the seeder path, ADR-12). Added so admins can add products
/// without a seed-file redeploy — previously the only way to insert a <see cref="Domain.Catalog.Product"/>
/// row was <c>CatalogSeeder</c>. Throws <see cref="DomainConflictException"/> (mapped to 409) for a
/// duplicate slug, matching the unique index on <c>Product.Slug</c>; other invariant failures surface
/// as <c>DomainException</c> (422), same as <see cref="UpdateProduct"/>.
/// </summary>
public sealed class CreateProduct
{
    private readonly IAppDbContext _db;
    private readonly ImageUrlBuilder _imageUrlBuilder;

    public CreateProduct(IAppDbContext db, ImageUrlBuilder imageUrlBuilder)
    {
        _db = db;
        _imageUrlBuilder = imageUrlBuilder;
    }

    public async Task<AdminProduct> ExecuteAsync(
        string slug,
        string name,
        string platform,
        string? description,
        string? imageKey,
        string? detailImageKey,
        bool isActive,
        IReadOnlyList<string>? imageKeys = null,
        string? activationGuideUrl = null,
        string? activationType = null,
        CancellationToken cancellationToken = default)
    {
        var slugTaken = await _db.Products.AnyAsync(p => p.Slug == slug, cancellationToken);
        if (slugTaken)
        {
            throw new DomainConflictException($"A product with slug '{slug}' already exists.");
        }

        var product = new Product(slug, name, platform, isActive, imageKey, description, detailImageKey, activationGuideUrl, activationType);
        _db.Products.Add(product);

        var images = (imageKeys ?? [])
            .Select((key, index) => new ProductImage(product.Id, key, index))
            .ToList();
        await _db.ProductImages.AddRangeAsync(images, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return ListAdminProducts.ToAdminProduct(product, [], images, _imageUrlBuilder);
    }
}
