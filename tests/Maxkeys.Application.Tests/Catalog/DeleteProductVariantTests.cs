using Maxkeys.Application.Catalog;
using Maxkeys.Application.Tests.Fixtures;

namespace Maxkeys.Application.Tests.Catalog;

[Collection(PostgresCollection.Name)]
public sealed class DeleteProductVariantTests
{
    private readonly PostgresFixture _fixture;

    public DeleteProductVariantTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Removes_the_variant_row()
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
        var sut = new DeleteProductVariant(context);

        var deleted = await sut.ExecuteAsync(variantId);

        Assert.True(deleted);
        await using var verify = _fixture.CreateContext();
        Assert.Null(await verify.ProductVariants.FindAsync(variantId));
    }

    [Fact]
    public async Task Returns_false_for_unknown_id()
    {
        await using var context = _fixture.CreateContext();
        var sut = new DeleteProductVariant(context);

        var deleted = await sut.ExecuteAsync(Guid.NewGuid());

        Assert.False(deleted);
    }
}
