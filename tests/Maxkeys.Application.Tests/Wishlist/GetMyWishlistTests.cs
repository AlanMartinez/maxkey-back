using Maxkeys.Application.Catalog;
using Maxkeys.Application.Tests.Catalog;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Application.Wishlist;
using Maxkeys.Domain.Wishlist;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Tests.Wishlist;

[Collection(PostgresCollection.Name)]
public sealed class GetMyWishlistTests
{
    private readonly PostgresFixture _fixture;
    private readonly ImageUrlBuilder _imageUrlBuilder;

    public GetMyWishlistTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _imageUrlBuilder = new ImageUrlBuilder(
            Options.Create(new StorageOptions { R2PublicBaseUrl = "https://img.test" }));
    }

    [Fact]
    public async Task Listing_returns_only_the_caller_items_newest_first()
    {
        var userId = Guid.NewGuid();
        var otherUser = Guid.NewGuid();
        var platform = CatalogTestData.UniquePlatform();

        Guid olderProductId, newerProductId;
        await using (var seed = _fixture.CreateContext())
        {
            var older = CatalogTestData.SeedProduct(seed, platform, isActive: true);
            CatalogTestData.SeedVariant(seed, older.Id, price: 10m);
            var newer = CatalogTestData.SeedProduct(seed, platform, isActive: true);
            CatalogTestData.SeedVariant(seed, newer.Id, price: 20m);
            var otherUsersProduct = CatalogTestData.SeedProduct(seed, platform, isActive: true);
            CatalogTestData.SeedVariant(seed, otherUsersProduct.Id, price: 30m);

            seed.WishlistItems.Add(new WishlistItem(userId, older.Id, DateTimeOffset.UtcNow.AddMinutes(-2)));
            seed.WishlistItems.Add(new WishlistItem(userId, newer.Id, DateTimeOffset.UtcNow.AddMinutes(-1)));
            seed.WishlistItems.Add(new WishlistItem(otherUser, otherUsersProduct.Id, DateTimeOffset.UtcNow));
            await seed.SaveChangesAsync();

            olderProductId = older.Id;
            newerProductId = newer.Id;
        }

        await using var context = _fixture.CreateContext();
        var sut = new GetMyWishlist(context, _imageUrlBuilder);

        var result = await sut.ExecuteAsync(userId);

        Assert.Equal([newerProductId, olderProductId], result.Select(r => r.ProductId));
    }

    [Fact]
    public async Task Listing_excludes_inactive_products()
    {
        var userId = Guid.NewGuid();
        Guid productId;
        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(seed, CatalogTestData.UniquePlatform(), isActive: true);
            CatalogTestData.SeedVariant(seed, product.Id, price: 10m);
            seed.WishlistItems.Add(new WishlistItem(userId, product.Id, DateTimeOffset.UtcNow));
            await seed.SaveChangesAsync();
            productId = product.Id;

            product.UpdateCatalogInfo(product.Name, product.Platform, product.Description, product.ImageKey, product.DetailImageKey, isActive: false, product.ActivationGuideId, product.ActivationType);
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new GetMyWishlist(context, _imageUrlBuilder);

        var result = await sut.ExecuteAsync(userId);

        Assert.DoesNotContain(result, r => r.ProductId == productId);
    }

    [Fact]
    public async Task FromPrice_uses_the_recommended_variant_over_a_cheaper_one()
    {
        var userId = Guid.NewGuid();
        Guid productId;
        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(seed, CatalogTestData.UniquePlatform(), isActive: true);
            CatalogTestData.SeedVariant(seed, product.Id, price: 100m, sortOrder: 0);
            CatalogTestData.SeedVariant(seed, product.Id, price: 200m, discountPercentage: 20m, sortOrder: 1, isRecommended: true);
            seed.WishlistItems.Add(new WishlistItem(userId, product.Id, DateTimeOffset.UtcNow));
            await seed.SaveChangesAsync();
            productId = product.Id;
        }

        await using var context = _fixture.CreateContext();
        var sut = new GetMyWishlist(context, _imageUrlBuilder);

        var result = await sut.ExecuteAsync(userId);

        var summary = Assert.Single(result, r => r.ProductId == productId);
        Assert.Equal(200m, summary.FromPrice);
        Assert.Equal(250m, summary.OldPrice);
    }

    [Fact]
    public async Task Empty_wishlist_returns_empty_list()
    {
        await using var context = _fixture.CreateContext();
        var sut = new GetMyWishlist(context, _imageUrlBuilder);

        var result = await sut.ExecuteAsync(Guid.NewGuid());

        Assert.Empty(result);
    }
}
