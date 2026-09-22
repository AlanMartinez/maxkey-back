using Maxkeys.Application.Persistence;
using Maxkeys.Domain.Wishlist;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Wishlist;

/// <summary>
/// Adds a product to the caller's wishlist (wishlist spec). Returns
/// <see langword="false"/> for an unknown or inactive product (API maps to
/// 404) — mirrors <see cref="Maxkeys.Application.Catalog.GetProductBySlug"/>'s
/// not-found handling. Idempotent: adding a product already on the wishlist
/// is not an error (spec: "AddToWishlist is idempotent").
/// </summary>
public sealed class AddToWishlist
{
    private readonly IAppDbContext _db;

    public AddToWishlist(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<bool> ExecuteAsync(Guid userId, Guid productId, CancellationToken cancellationToken = default)
    {
        var productIsActive = await _db.Products
            .AnyAsync(p => p.Id == productId && p.IsActive, cancellationToken);
        if (!productIsActive)
        {
            return false;
        }

        var alreadyOnWishlist = await _db.WishlistItems
            .AnyAsync(w => w.UserId == userId && w.ProductId == productId, cancellationToken);
        if (alreadyOnWishlist)
        {
            return true;
        }

        _db.WishlistItems.Add(new WishlistItem(userId, productId, DateTimeOffset.UtcNow));

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A concurrent add for the same (userId, productId) raced us past the
            // AnyAsync check above and won; the unique index already has the row,
            // so this is still a success (idempotent add), not a failure.
            if (_db is DbContext dbContext)
            {
                dbContext.ChangeTracker.Clear();
            }
        }

        return true;
    }
}
