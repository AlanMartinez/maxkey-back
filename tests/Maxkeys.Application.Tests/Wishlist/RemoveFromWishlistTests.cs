using Maxkeys.Application.Tests.Catalog;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Application.Wishlist;
using Maxkeys.Domain.Wishlist;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Tests.Wishlist;

[Collection(PostgresCollection.Name)]
public sealed class RemoveFromWishlistTests
{
    private readonly PostgresFixture _fixture;

    public RemoveFromWishlistTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Removing_an_existing_item_deletes_it()
    {
        var userId = Guid.NewGuid();
        Guid productId;
        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(seed, CatalogTestData.UniquePlatform(), isActive: true);
            seed.WishlistItems.Add(new WishlistItem(userId, product.Id, DateTimeOffset.UtcNow));
            await seed.SaveChangesAsync();
            productId = product.Id;
        }

        await using var context = _fixture.CreateContext();
        await new RemoveFromWishlist(context).ExecuteAsync(userId, productId);

        await using var verify = _fixture.CreateContext();
        Assert.False(await verify.WishlistItems.AnyAsync(w => w.UserId == userId && w.ProductId == productId));
    }

    [Fact]
    public async Task Removing_a_nonexistent_item_does_not_throw()
    {
        await using var context = _fixture.CreateContext();

        await new RemoveFromWishlist(context).ExecuteAsync(Guid.NewGuid(), Guid.NewGuid());
    }

    [Fact]
    public async Task Removing_only_affects_the_calling_user()
    {
        var owner = Guid.NewGuid();
        var otherUser = Guid.NewGuid();
        Guid productId;
        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(seed, CatalogTestData.UniquePlatform(), isActive: true);
            seed.WishlistItems.Add(new WishlistItem(owner, product.Id, DateTimeOffset.UtcNow));
            await seed.SaveChangesAsync();
            productId = product.Id;
        }

        await using var context = _fixture.CreateContext();
        await new RemoveFromWishlist(context).ExecuteAsync(otherUser, productId);

        await using var verify = _fixture.CreateContext();
        Assert.True(await verify.WishlistItems.AnyAsync(w => w.UserId == owner && w.ProductId == productId));
    }
}
