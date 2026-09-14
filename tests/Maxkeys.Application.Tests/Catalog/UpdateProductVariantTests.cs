using Maxkeys.Application.Catalog;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Common;

namespace Maxkeys.Application.Tests.Catalog;

[Collection(PostgresCollection.Name)]
public sealed class UpdateProductVariantTests
{
    private readonly PostgresFixture _fixture;

    public UpdateProductVariantTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Updates_price_and_active_flag()
    {
        Guid variantId;
        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(seed, CatalogTestData.UniquePlatform(), isActive: true);
            await seed.SaveChangesAsync();
            CatalogTestData.SeedVariant(seed, product.Id, price: 100m, isActive: true);
            await seed.SaveChangesAsync();
            variantId = seed.ProductVariants.Single(v => v.ProductId == product.Id).Id;
        }

        await using var context = _fixture.CreateContext();
        var sut = new UpdateProductVariant(context);

        var updated = await sut.ExecuteAsync(
            variantId, price: 250m, oldPrice: null, currency: "ARS", region: "AR", edition: "Deluxe", sortOrder: 1, isActive: false);

        Assert.NotNull(updated);
        Assert.Equal(250m, updated!.Price);
        Assert.False(updated.IsActive);
    }

    [Fact]
    public async Task Invalid_price_throws_domain_exception_and_leaves_variant_unchanged()
    {
        Guid variantId;
        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(seed, CatalogTestData.UniquePlatform(), isActive: true);
            await seed.SaveChangesAsync();
            CatalogTestData.SeedVariant(seed, product.Id, price: 100m, isActive: true);
            await seed.SaveChangesAsync();
            variantId = seed.ProductVariants.Single(v => v.ProductId == product.Id).Id;
        }

        await using var context = _fixture.CreateContext();
        var sut = new UpdateProductVariant(context);

        await Assert.ThrowsAsync<DomainException>(
            () => sut.ExecuteAsync(variantId, price: 0m, oldPrice: null, currency: "ARS", region: null, edition: null, sortOrder: 0, isActive: true));

        await using var verify = _fixture.CreateContext();
        var reloaded = await verify.ProductVariants.FindAsync(variantId);
        Assert.Equal(100m, reloaded!.Price);
    }

    [Fact]
    public async Task Returns_null_for_unknown_id()
    {
        await using var context = _fixture.CreateContext();
        var sut = new UpdateProductVariant(context);

        var updated = await sut.ExecuteAsync(
            Guid.NewGuid(), price: 100m, oldPrice: null, currency: "ARS", region: null, edition: null, sortOrder: 0, isActive: true);

        Assert.Null(updated);
    }
}
