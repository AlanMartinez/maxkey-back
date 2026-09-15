using Maxkeys.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Catalog;

/// <summary>
/// Soft-deletes a variant by flipping <c>IsActive</c> to <see langword="false"/>
/// via <see cref="Domain.Catalog.ProductVariant.UpdateDetails"/>, preserving
/// every other field (admin-catalog spec "Admin Variant Soft-Delete" and
/// "Uniform Soft-Delete Guarantee" — the row and its references are never
/// hard-deleted). Returns <see langword="null"/> for an unknown id so the
/// endpoint can map it to 404. One class per use case (ADR-02).
/// </summary>
public sealed class DeleteProductVariant
{
    private readonly IAppDbContext _db;

    public DeleteProductVariant(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<AdminVariant?> ExecuteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var variant = await _db.ProductVariants.SingleOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (variant is null)
        {
            return null;
        }

        variant.UpdateDetails(variant.Price, variant.DiscountPercentage, variant.Currency, variant.Region, variant.Edition, variant.SortOrder, isActive: false);
        await _db.SaveChangesAsync(cancellationToken);

        return ListAdminProducts.ToAdminVariant(variant);
    }
}
