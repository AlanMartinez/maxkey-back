using Maxkeys.Application.Catalog;
using Maxkeys.Application.Tests.Fixtures;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Tests.Catalog;

[Collection(PostgresCollection.Name)]
public sealed class ListAdminProductsTests
{
    private readonly PostgresFixture _fixture;
    private readonly ImageUrlBuilder _imageUrlBuilder;

    public ListAdminProductsTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _imageUrlBuilder = new ImageUrlBuilder(
            Options.Create(new StorageOptions { R2PublicBaseUrl = "https://img.test" }));
    }

    [Fact]
    public async Task Includes_inactive_products_and_inactive_variants()
    {
        var platform = CatalogTestData.UniquePlatform();

        Guid activeProductId, inactiveProductId;
        await using (var seed = _fixture.CreateContext())
        {
            var active = CatalogTestData.SeedProduct(seed, platform, isActive: true);
            var inactive = CatalogTestData.SeedProduct(seed, platform, isActive: false);
            CatalogTestData.SeedVariant(seed, active.Id, price: 100m, isActive: true);
            CatalogTestData.SeedVariant(seed, active.Id, price: 200m, isActive: false);
            activeProductId = active.Id;
            inactiveProductId = inactive.Id;
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new ListAdminProducts(context, _imageUrlBuilder);

        var products = await sut.ExecuteAsync();

        var activeProduct = Assert.Single(products, p => p.Id == activeProductId);
        Assert.Equal(2, activeProduct.Variants.Count);
        Assert.Contains(products, p => p.Id == inactiveProductId && !p.IsActive);
    }
}
