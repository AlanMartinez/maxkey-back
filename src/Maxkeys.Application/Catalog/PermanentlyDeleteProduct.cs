using Maxkeys.Application.Persistence;
using Maxkeys.Domain.Keys;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Catalog;

/// <summary>
/// Hard-deletes a product together with its variants, gallery images, wishlist
/// entries and carousel slides in a single <c>SaveChanges</c> (admin-catalog
/// "Eliminar definitivamente"), unlike <see cref="DeleteProduct"/> which only
/// deactivates it. Safe against order history for the same reason as
/// <see cref="DeleteProductVariant"/>: <c>OrderItem</c> keeps a loose
/// <c>ProductVariantId</c> plus a name/price snapshot. Unsold (Available) keys
/// of the product's variants block the delete with
/// <see cref="PermanentlyDeleteProductOutcome.Conflict"/> unless
/// <c>force</c> is set, in which case they are kept and orphaned via
/// <see cref="Key.Orphan"/>; Assigned/Revealed keys keep their variant id
/// untouched. One class per use case (ADR-02).
/// </summary>
public sealed class PermanentlyDeleteProduct
{
    private readonly IAppDbContext _db;

    public PermanentlyDeleteProduct(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<PermanentlyDeleteProductResult> ExecuteAsync(
        Guid id, bool force, CancellationToken cancellationToken = default)
    {
        var product = await _db.Products.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
        {
            return PermanentlyDeleteProductResult.NotFound;
        }

        var variants = await _db.ProductVariants
            .Where(v => v.ProductId == id)
            .ToListAsync(cancellationToken);
        var variantIds = variants.Select(v => v.Id).ToList();

        var availableKeys = await _db.Keys
            .Where(k => k.ProductVariantId != null
                && variantIds.Contains(k.ProductVariantId.Value)
                && k.Status == KeyStatus.Available)
            .ToListAsync(cancellationToken);

        if (availableKeys.Count > 0 && !force)
        {
            return PermanentlyDeleteProductResult.Conflict(availableKeys.Count);
        }

        foreach (var key in availableKeys)
        {
            key.Orphan();
        }

        _db.WishlistItems.RemoveRange(
            await _db.WishlistItems.Where(w => w.ProductId == id).ToListAsync(cancellationToken));
        _db.CarouselSlides.RemoveRange(
            await _db.CarouselSlides.Where(s => s.ProductId == id).ToListAsync(cancellationToken));
        _db.ProductImages.RemoveRange(
            await _db.ProductImages.Where(i => i.ProductId == id).ToListAsync(cancellationToken));
        _db.ProductVariants.RemoveRange(variants);
        _db.Products.Remove(product);

        await _db.SaveChangesAsync(cancellationToken);

        return PermanentlyDeleteProductResult.Deleted;
    }
}

/// <summary>Outcome of <see cref="PermanentlyDeleteProduct.ExecuteAsync"/>.</summary>
public enum PermanentlyDeleteProductOutcome
{
    Deleted,
    NotFound,
    Conflict,
}

/// <summary>
/// Result of <see cref="PermanentlyDeleteProduct.ExecuteAsync"/>.
/// <see cref="AvailableKeyCount"/> is the number of unsold keys that blocked the
/// delete when <see cref="Outcome"/> is <see cref="PermanentlyDeleteProductOutcome.Conflict"/>, otherwise 0.
/// </summary>
public sealed record PermanentlyDeleteProductResult(PermanentlyDeleteProductOutcome Outcome, int AvailableKeyCount = 0)
{
    public static readonly PermanentlyDeleteProductResult Deleted = new(PermanentlyDeleteProductOutcome.Deleted);
    public static readonly PermanentlyDeleteProductResult NotFound = new(PermanentlyDeleteProductOutcome.NotFound);

    public static PermanentlyDeleteProductResult Conflict(int availableKeyCount) =>
        new(PermanentlyDeleteProductOutcome.Conflict, availableKeyCount);
}
