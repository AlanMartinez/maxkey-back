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
/// the throw happens before <c>SaveChangesAsync</c>. The recommended flag is
/// exclusive per product: marking a variant recommended first clears the flag on
/// every sibling of the same <c>ProductId</c> and persists that, then flags the
/// target in a second <c>SaveChangesAsync</c>. The two saves are deliberate: EF
/// does not guarantee statement order inside one batch, and Postgres checks the
/// partial unique index (<c>ix_product_variants_product_id_is_recommended</c>)
/// per statement, so a single save intermittently failed with 23505 when the
/// target's UPDATE ran before the sibling's. If the second save fails the product
/// is left with no recommended variant, and the public read side falls back to
/// the cheapest active one. Passing <see langword="false"/> simply un-marks the variant.
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
        decimal? discountPercentage,
        string currency,
        string? region,
        string? edition,
        int sortOrder,
        bool isActive,
        bool isRecommended,
        CancellationToken cancellationToken = default)
    {
        var variant = await _db.ProductVariants.SingleOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (variant is null)
        {
            return null;
        }

        var product = await _db.Products.SingleAsync(p => p.Id == variant.ProductId, cancellationToken);
        product.Touch();

        variant.UpdateDetails(price, discountPercentage, currency, region, edition, sortOrder, isActive);

        if (isRecommended)
        {
            var recommendedSiblings = await _db.ProductVariants
                .Where(v => v.ProductId == variant.ProductId && v.IsRecommended && v.Id != id)
                .ToListAsync(cancellationToken);

            if (recommendedSiblings.Count > 0)
            {
                foreach (var sibling in recommendedSiblings)
                {
                    sibling.SetRecommended(false);
                }

                // Persist the un-marks on their own so the partial unique index never sees
                // two recommended rows for the product, whatever order EF batches the UPDATEs.
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        variant.SetRecommended(isRecommended);
        await _db.SaveChangesAsync(cancellationToken);

        return ListAdminProducts.ToAdminVariant(variant);
    }
}
