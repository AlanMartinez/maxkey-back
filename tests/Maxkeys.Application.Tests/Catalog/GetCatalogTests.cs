using Maxkeys.Application.Catalog;
using Maxkeys.Application.Tests.Fixtures;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Tests.Catalog;

[Collection(PostgresCollection.Name)]
public sealed class GetCatalogTests
{
    private readonly PostgresFixture _fixture;
    private readonly ImageUrlBuilder _imageUrlBuilder;

    public GetCatalogTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _imageUrlBuilder = new ImageUrlBuilder(
            Options.Create(new StorageOptions { R2PublicBaseUrl = "https://img.test" }));
    }

    [Fact]
    public async Task Listing_returns_only_active_products_with_variants()
    {
        var platform = CatalogTestData.UniquePlatform();

        Guid activeId;
        await using (var seed = _fixture.CreateContext())
        {
            var active = CatalogTestData.SeedProduct(seed, platform, isActive: true);
            CatalogTestData.SeedVariant(seed, active.Id, price: 100m, discountPercentage: 20m, sortOrder: 0);
            CatalogTestData.SeedVariant(seed, active.Id, price: 200m, sortOrder: 1);

            var inactive = CatalogTestData.SeedProduct(seed, platform, isActive: false);
            CatalogTestData.SeedVariant(seed, inactive.Id, price: 50m);

            await seed.SaveChangesAsync();
            activeId = active.Id;
        }

        await using var context = _fixture.CreateContext();
        var sut = new GetCatalog(context, _imageUrlBuilder);

        var result = await sut.ExecuteAsync(platform: null, q: null);
        var matches = result.Where(p => p.Platform == platform).ToList();

        var summary = Assert.Single(matches);
        Assert.Equal(activeId, summary.Id);
        Assert.Equal(100m, summary.FromPrice);
        Assert.Equal(125m, summary.OldPrice);
    }

    [Fact]
    public async Task Platform_filter_returns_only_matching_products()
    {
        var platformA = CatalogTestData.UniquePlatform();
        var platformB = CatalogTestData.UniquePlatform();

        await using (var seed = _fixture.CreateContext())
        {
            var productA = CatalogTestData.SeedProduct(seed, platformA, isActive: true);
            CatalogTestData.SeedVariant(seed, productA.Id, price: 10m);

            var productB = CatalogTestData.SeedProduct(seed, platformB, isActive: true);
            CatalogTestData.SeedVariant(seed, productB.Id, price: 20m);

            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new GetCatalog(context, _imageUrlBuilder);

        var result = await sut.ExecuteAsync(platform: platformA, q: null);

        Assert.Contains(result, p => p.Platform == platformA);
        Assert.DoesNotContain(result, p => p.Platform == platformB);
    }

    [Fact]
    public async Task Search_by_name_is_case_insensitive()
    {
        var platform = CatalogTestData.UniquePlatform();
        var uniqueToken = $"FcPoints{Guid.NewGuid():N}";

        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(seed, platform, isActive: true, name: $"{uniqueToken} Card");
            CatalogTestData.SeedVariant(seed, product.Id, price: 10m);
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new GetCatalog(context, _imageUrlBuilder);

        var result = await sut.ExecuteAsync(platform: null, q: uniqueToken.ToLowerInvariant());

        Assert.Contains(result, p => p.Platform == platform);
    }

    [Fact]
    public async Task ImageUrl_is_composed_from_base_url_and_image_key()
    {
        var platform = CatalogTestData.UniquePlatform();

        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(seed, platform, isActive: true, imageKey: "products/abc.png");
            CatalogTestData.SeedVariant(seed, product.Id, price: 100m);
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new GetCatalog(context, _imageUrlBuilder);

        var result = await sut.ExecuteAsync(platform: platform, q: null);

        var summary = Assert.Single(result);
        Assert.Equal("https://img.test/products/abc.png", summary.ImageUrl);
    }
}
