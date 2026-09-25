using Maxkeys.Application.Persistence;
using Maxkeys.Application.Tests.Catalog;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Application.Wishlist;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Tests.Wishlist;

[Collection(PostgresCollection.Name)]
public sealed class AddToWishlistTests
{
    private readonly PostgresFixture _fixture;

    public AddToWishlistTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Adding_an_active_product_creates_a_row()
    {
        var userId = Guid.NewGuid();
        Guid productId;
        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(seed, CatalogTestData.UniquePlatform(), isActive: true);
            await seed.SaveChangesAsync();
            productId = product.Id;
        }

        await using var context = _fixture.CreateContext();
        var sut = new AddToWishlist(context);

        var result = await sut.ExecuteAsync(userId, productId);

        Assert.True(result);
        await using var verify = _fixture.CreateContext();
        Assert.True(await verify.WishlistItems.AnyAsync(w => w.UserId == userId && w.ProductId == productId));
    }

    [Fact]
    public async Task Adding_an_unknown_product_returns_false()
    {
        await using var context = _fixture.CreateContext();
        var sut = new AddToWishlist(context);

        var result = await sut.ExecuteAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.False(result);
    }

    [Fact]
    public async Task Adding_an_inactive_product_returns_false()
    {
        Guid productId;
        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(seed, CatalogTestData.UniquePlatform(), isActive: false);
            await seed.SaveChangesAsync();
            productId = product.Id;
        }

        await using var context = _fixture.CreateContext();
        var sut = new AddToWishlist(context);

        var result = await sut.ExecuteAsync(Guid.NewGuid(), productId);

        Assert.False(result);
    }

    [Fact]
    public async Task Adding_the_same_product_twice_is_idempotent()
    {
        var userId = Guid.NewGuid();
        Guid productId;
        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(seed, CatalogTestData.UniquePlatform(), isActive: true);
            await seed.SaveChangesAsync();
            productId = product.Id;
        }

        await using (var first = _fixture.CreateContext())
        {
            Assert.True(await new AddToWishlist(first).ExecuteAsync(userId, productId));
        }

        await using var second = _fixture.CreateContext();
        var result = await new AddToWishlist(second).ExecuteAsync(userId, productId);

        Assert.True(result);
        await using var verify = _fixture.CreateContext();
        Assert.Equal(1, await verify.WishlistItems.CountAsync(w => w.UserId == userId && w.ProductId == productId));
    }

    [Fact]
    public async Task Concurrent_adds_of_the_same_product_both_succeed_and_only_one_row_is_created()
    {
        var userId = Guid.NewGuid();
        Guid productId;
        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(seed, CatalogTestData.UniquePlatform(), isActive: true);
            await seed.SaveChangesAsync();
            productId = product.Id;
        }

        await using var first = _fixture.CreateContext();
        await using var second = _fixture.CreateContext();

        var firstTask = new AddToWishlist(first).ExecuteAsync(userId, productId);
        var secondTask = new AddToWishlist(second).ExecuteAsync(userId, productId);
        var results = await Task.WhenAll(firstTask, secondTask);

        Assert.All(results, Assert.True);
        await using var verify = _fixture.CreateContext();
        Assert.Equal(1, await verify.WishlistItems.CountAsync(w => w.UserId == userId && w.ProductId == productId));
    }
}
