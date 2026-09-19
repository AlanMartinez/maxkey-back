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
            variantId, price: 250m, discountPercentage: null, currency: "ARS", region: "AR", edition: "Deluxe", sortOrder: 1, isActive: false, isRecommended: false);

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
            () => sut.ExecuteAsync(variantId, price: 0m, discountPercentage: null, currency: "ARS", region: null, edition: null, sortOrder: 0, isActive: true, isRecommended: false));

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
            Guid.NewGuid(), price: 100m, discountPercentage: null, currency: "ARS", region: null, edition: null, sortOrder: 0, isActive: true, isRecommended: false);

        Assert.Null(updated);
    }

    [Fact]
    public async Task Marking_recommended_clears_the_previous_sibling_and_leaves_other_products_alone()
    {
        Guid previousId, targetId, otherProductVariantId;
        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(seed, CatalogTestData.UniquePlatform(), isActive: true);
            var otherProduct = CatalogTestData.SeedProduct(seed, CatalogTestData.UniquePlatform(), isActive: true);
            await seed.SaveChangesAsync();
            CatalogTestData.SeedVariant(seed, product.Id, price: 100m, sortOrder: 0, isRecommended: true);
            CatalogTestData.SeedVariant(seed, product.Id, price: 200m, sortOrder: 1);
            CatalogTestData.SeedVariant(seed, otherProduct.Id, price: 300m, isRecommended: true);
            await seed.SaveChangesAsync();
            previousId = seed.ProductVariants.Single(v => v.ProductId == product.Id && v.SortOrder == 0).Id;
            targetId = seed.ProductVariants.Single(v => v.ProductId == product.Id && v.SortOrder == 1).Id;
            otherProductVariantId = seed.ProductVariants.Single(v => v.ProductId == otherProduct.Id).Id;
        }

        await using var context = _fixture.CreateContext();
        var sut = new UpdateProductVariant(context);

        var updated = await sut.ExecuteAsync(
            targetId, price: 200m, discountPercentage: null, currency: "ARS", region: null, edition: null, sortOrder: 1, isActive: true, isRecommended: true);

        Assert.NotNull(updated);
        Assert.True(updated!.IsRecommended);

        await using var verify = _fixture.CreateContext();
        Assert.True((await verify.ProductVariants.FindAsync(targetId))!.IsRecommended);
        Assert.False((await verify.ProductVariants.FindAsync(previousId))!.IsRecommended);
        Assert.True((await verify.ProductVariants.FindAsync(otherProductVariantId))!.IsRecommended);
    }

    [Fact]
    public async Task Passing_false_unmarks_the_recommended_variant()
    {
        Guid variantId;
        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(seed, CatalogTestData.UniquePlatform(), isActive: true);
            await seed.SaveChangesAsync();
            CatalogTestData.SeedVariant(seed, product.Id, price: 100m, isRecommended: true);
            await seed.SaveChangesAsync();
            variantId = seed.ProductVariants.Single(v => v.ProductId == product.Id).Id;
        }

        await using var context = _fixture.CreateContext();
        var sut = new UpdateProductVariant(context);

        var updated = await sut.ExecuteAsync(
            variantId, price: 100m, discountPercentage: null, currency: "ARS", region: null, edition: null, sortOrder: 0, isActive: true, isRecommended: false);

        Assert.NotNull(updated);
        Assert.False(updated!.IsRecommended);

        await using var verify = _fixture.CreateContext();
        Assert.False((await verify.ProductVariants.FindAsync(variantId))!.IsRecommended);
    }
}
