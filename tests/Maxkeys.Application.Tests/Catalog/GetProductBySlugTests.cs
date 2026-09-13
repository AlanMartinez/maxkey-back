using Maxkeys.Application.Catalog;
using Maxkeys.Application.Tests.Fixtures;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Tests.Catalog;

[Collection(PostgresCollection.Name)]
public sealed class GetProductBySlugTests
{
    private readonly PostgresFixture _fixture;
    private readonly ImageUrlBuilder _imageUrlBuilder;

    public GetProductBySlugTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _imageUrlBuilder = new ImageUrlBuilder(
            Options.Create(new StorageOptions { R2PublicBaseUrl = "https://img.test" }));
    }

    [Fact]
    public async Task Returns_detail_with_active_variants_ordered_by_sort_order()
    {
        var platform = CatalogTestData.UniquePlatform();
        var slug = $"slug-{Guid.NewGuid():N}";

        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(seed, platform, isActive: true, slug: slug);
            CatalogTestData.SeedVariant(seed, product.Id, price: 300m, sortOrder: 1, region: "AR", edition: "Deluxe");
            CatalogTestData.SeedVariant(seed, product.Id, price: 100m, oldPrice: 150m, sortOrder: 0, region: "AR", edition: "Standard");
            CatalogTestData.SeedVariant(seed, product.Id, price: 999m, sortOrder: 2, isActive: false);
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new GetProductBySlug(context, _imageUrlBuilder);

        var detail = await sut.ExecuteAsync(slug);

        Assert.NotNull(detail);
        Assert.Equal(2, detail!.Variants.Count);
        Assert.Equal(100m, detail.FromPrice);
        Assert.Equal(150m, detail.OldPrice);
        Assert.Equal("AR · Standard", detail.Variants[0].Name);
        Assert.Equal("AR · Deluxe", detail.Variants[1].Name);
    }

    [Fact]
    public async Task Returns_null_for_unknown_slug()
    {
        await using var context = _fixture.CreateContext();
        var sut = new GetProductBySlug(context, _imageUrlBuilder);

        var detail = await sut.ExecuteAsync($"does-not-exist-{Guid.NewGuid():N}");

        Assert.Null(detail);
    }

    [Fact]
    public async Task Returns_null_for_inactive_product()
    {
        var slug = $"slug-{Guid.NewGuid():N}";

        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(seed, CatalogTestData.UniquePlatform(), isActive: false, slug: slug);
            CatalogTestData.SeedVariant(seed, product.Id, price: 100m);
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new GetProductBySlug(context, _imageUrlBuilder);

        var detail = await sut.ExecuteAsync(slug);

        Assert.Null(detail);
    }
}
