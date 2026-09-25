using Maxkeys.Application.Persistence;
using Maxkeys.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Catalog;

/// <summary>
/// Creates a new variant for an existing product (admin-catalog spec "Admin
/// Variant Creation"). Returns <see langword="null"/> when the parent product
/// does not exist so the endpoint can map it to 404 (design D3) — intentionally
/// diverging from <c>CreateCarouselSlide</c>'s "unknown parent" 422 pattern.
/// Domain invariant failures (non-positive price, non-whitelisted currency,
/// out-of-range discount) surface as <c>DomainException</c>, mapped to 422 by
/// <c>ProblemDetailsExceptionHandler</c>. One class per use case (ADR-02).
/// </summary>
public sealed class CreateProductVariant
{
    private readonly IAppDbContext _db;

    public CreateProductVariant(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<AdminVariant?> ExecuteAsync(
        Guid productId,
        string? region,
        string? edition,
        decimal price,
        decimal? discountPercentage,
        string currency,
        int sortOrder,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var product = await _db.Products.SingleOrDefaultAsync(p => p.Id == productId, cancellationToken);
        if (product is null)
        {
            return null;
        }

        var variant = new ProductVariant(productId, price, currency, discountPercentage, region, edition, sortOrder, isActive);
        _db.ProductVariants.Add(variant);
        product.Touch();
        await _db.SaveChangesAsync(cancellationToken);

        return ListAdminProducts.ToAdminVariant(variant);
    }
}
