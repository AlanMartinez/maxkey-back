using Maxkeys.Application.Persistence;
using Maxkeys.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Catalog;

/// <summary>
/// Creates a new admin-managed product (admin-catalog spec "Admin Product
/// Creation"). Pre-checks slug uniqueness and returns <see langword="null"/>
/// when a duplicate exists so the endpoint can map it to 409 (design D3) — the
/// database unique index remains the backstop for the rare concurrent-create
/// race (design "Open Questions"). Domain invariant failures (empty name,
/// non-lowercase slug, etc.) surface as <c>DomainException</c>, mapped to 422
/// by <c>ProblemDetailsExceptionHandler</c>. One class per use case (ADR-02).
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

    public async Task<AdminProduct?> ExecuteAsync(
        string slug,
        string name,
        string platform,
        string? description,
        string? imageKey,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var slugExists = await _db.Products.AnyAsync(p => p.Slug == slug, cancellationToken);
        if (slugExists)
        {
            return null;
        }

        var product = new Product(slug, name, platform, isActive, imageKey, description);
        _db.Products.Add(product);
        await _db.SaveChangesAsync(cancellationToken);

        return ListAdminProducts.ToAdminProduct(product, [], _imageUrlBuilder);
    }
}
