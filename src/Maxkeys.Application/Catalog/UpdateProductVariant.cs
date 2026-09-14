using Maxkeys.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Catalog;

/// <summary>
/// Updates a variant's admin-editable fields via
/// <see cref="Domain.Catalog.ProductVariant.UpdateDetails"/> — the same domain
/// method the seeder uses (design D3: reuse, no new domain method; admin-catalog
/// spec "Admin Variant Activation Toggle"). Returns <see langword="null"/> for an
/// unknown id so the endpoint can map it to 404. Domain validation failures
/// surface as <c>DomainException</c>, mapped to 422 by
/// <c>ProblemDetailsExceptionHandler</c> — the variant stays unchanged because
/// the throw happens before <c>SaveChangesAsync</c>.
/// </summary>
public sealed class UpdateProductVariant
{
    private readonly IAppDbContext _db;

    public UpdateProductVariant(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<AdminVariant?> ExecuteAsync(
        Guid id,
        decimal price,
        decimal? oldPrice,
        string currency,
        string? region,
        string? edition,
        int sortOrder,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var variant = await _db.ProductVariants.SingleOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (variant is null)
        {
            return null;
        }

        variant.UpdateDetails(price, oldPrice, currency, region, edition, sortOrder, isActive);
        await _db.SaveChangesAsync(cancellationToken);

        return new AdminVariant(variant.Id, variant.Region, variant.Edition, variant.Price, variant.OldPrice, variant.Currency, variant.SortOrder, variant.IsActive);
    }
}
