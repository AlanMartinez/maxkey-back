using Maxkeys.Domain.Common;

namespace Maxkeys.Domain.Wishlist;

/// <summary>
/// A logged-in buyer's saved product (wishlist spec, decision 1). References
/// <see cref="ProductId"/> only, never a specific
/// <see cref="Maxkeys.Domain.Catalog.ProductVariant"/> — the displayed price
/// always resolves at read time from the product's current display variant
/// (<c>VariantPricing.PickDisplayVariant</c>), so a price or recommended-variant
/// change is reflected automatically without touching this row. No mutation
/// methods — a wishlist row is either present or removed.
/// </summary>
public sealed class WishlistItem : Entity
{
    public Guid UserId { get; private set; }
    public Guid ProductId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public WishlistItem(Guid userId, Guid productId, DateTimeOffset createdAt)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainException("Wishlist item must reference a user.");
        }

        if (productId == Guid.Empty)
        {
            throw new DomainException("Wishlist item must reference a product.");
        }

        UserId = userId;
        ProductId = productId;
        CreatedAt = createdAt;
    }
}
