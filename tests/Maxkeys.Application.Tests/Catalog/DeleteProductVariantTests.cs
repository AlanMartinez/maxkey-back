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

    /// <summary>Admin Variant Soft-Delete — "Variant soft-deleted", sibling fields preserved.</summary>
    [Fact]
    public async Task Soft_deletes_a_variant_preserving_sibling_fields()
    {
        Guid variantId;
        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(seed, CatalogTestData.UniquePlatform(), isActive: true);
            await seed.SaveChangesAsync();
            CatalogTestData.SeedVariant(seed, product.Id, price: 500m, discountPercentage: 10m, sortOrder: 2, region: "AR", edition: "Deluxe", isActive: true);
            await seed.SaveChangesAsync();
            variantId = seed.ProductVariants.Single(v => v.ProductId == product.Id).Id;
        }

        await using var context = _fixture.CreateContext();
        var sut = new DeleteProductVariant(context);

        var deleted = await sut.ExecuteAsync(variantId);

        Assert.NotNull(deleted);
        Assert.False(deleted!.IsActive);
        Assert.Equal(500m, deleted.Price);
        Assert.Equal("AR", deleted.Region);
        Assert.Equal("Deluxe", deleted.Edition);

        await using var verify = _fixture.CreateContext();
        var reloaded = await verify.ProductVariants.FindAsync(variantId);
        Assert.NotNull(reloaded);
        Assert.False(reloaded!.IsActive);
        Assert.Equal(500m, reloaded.Price);
    }

    /// <summary>Admin Variant Soft-Delete — "Variant not found".</summary>
    [Fact]
    public async Task Returns_null_for_unknown_id()
    {
        await using var context = _fixture.CreateContext();
        var sut = new DeleteProductVariant(context);

        var deleted = await sut.ExecuteAsync(Guid.NewGuid());

        Assert.Null(deleted);
    }
}
