using Maxkeys.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Catalog;

/// <summary>
/// Hard-deletes a variant row (admin-catalog UX: "Eliminar variante" removes it
/// outright, unlike product/variant deactivation which is a soft <c>isActive</c>
/// toggle via <see cref="UpdateProductVariant"/>). Safe against order history:
/// <c>OrderItem</c> only carries a loose <c>ProductVariantId</c> plus a name/price
/// snapshot (no FK, no navigation), so removing the variant row never touches
/// past orders. Returns <see langword="false"/> for an unknown id so the endpoint
/// can map it to 404.
/// </summary>
public sealed class DeleteProductVariant
{
    private readonly IAppDbContext _db;

    public DeleteProductVariant(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<bool> ExecuteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var variant = await _db.ProductVariants.SingleOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (variant is null)
        {
            return false;
        }

        _db.ProductVariants.Remove(variant);
        await _db.SaveChangesAsync(cancellationToken);

        return true;
    }
}
