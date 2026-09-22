using Maxkeys.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Wishlist;

/// <summary>
/// Removes a product from the caller's wishlist (wishlist spec, decision 2:
/// keyed by <paramref name="productId"/>, not the wishlist row's own id).
/// Always succeeds — removing a product that isn't on the wishlist is not an
/// error, so the API always returns 204, never 404.
/// </summary>
public sealed class RemoveFromWishlist
{
    private readonly IAppDbContext _db;

    public RemoveFromWishlist(IAppDbContext db)
    {
        _db = db;
    }

    public Task ExecuteAsync(Guid userId, Guid productId, CancellationToken cancellationToken = default) =>
        _db.WishlistItems
            .Where(w => w.UserId == userId && w.ProductId == productId)
            .ExecuteDeleteAsync(cancellationToken);
}
