using Maxkeys.Domain.Common;
using Maxkeys.Domain.Wishlist;

namespace Maxkeys.Domain.Tests.Wishlist;

public class WishlistItemTests
{
    [Fact]
    public void Constructor_WithValidData_CreatesItem()
    {
        var userId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;

        var item = new WishlistItem(userId, productId, createdAt);

        Assert.Equal(userId, item.UserId);
        Assert.Equal(productId, item.ProductId);
        Assert.Equal(createdAt, item.CreatedAt);
    }

    [Fact]
    public void Constructor_WithEmptyUserId_Throws()
    {
        Assert.Throws<DomainException>(() => new WishlistItem(Guid.Empty, Guid.NewGuid(), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Constructor_WithEmptyProductId_Throws()
    {
        Assert.Throws<DomainException>(() => new WishlistItem(Guid.NewGuid(), Guid.Empty, DateTimeOffset.UtcNow));
    }
}
