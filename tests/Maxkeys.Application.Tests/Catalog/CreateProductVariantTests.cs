using Maxkeys.Application.Catalog;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Common;

namespace Maxkeys.Application.Tests.Catalog;

[Collection(PostgresCollection.Name)]
public sealed class CreateProductVariantTests
{
    private readonly PostgresFixture _fixture;

    public CreateProductVariantTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Admin Variant Creation — "Variant created".</summary>
    [Fact]
    public async Task Creates_a_variant_for_an_existing_product()
    {
        Guid productId;
        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(seed, CatalogTestData.UniquePlatform(), isActive: true);
            productId = product.Id;
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new CreateProductVariant(context);

        var created = await sut.ExecuteAsync(
            productId, region: "AR", edition: "Standard", price: 1000m, discountPercentage: 20m, currency: "ARS", sortOrder: 1, isActive: true);

        Assert.NotNull(created);
        Assert.Equal(1000m, created!.Price);
        Assert.Equal(1250m, created.OldPrice);
        Assert.Equal("AR", created.Region);
        Assert.Equal("Standard", created.Edition);
        Assert.Equal(1, created.SortOrder);

        await using var verify = _fixture.CreateContext();
        var reloaded = await verify.ProductVariants.FindAsync(created.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(productId, reloaded!.ProductId);
    }

    /// <summary>Admin Variant Creation — "Parent product not found".</summary>
    [Fact]
    public async Task Returns_null_for_unknown_parent_product()
    {
        var unknownProductId = Guid.NewGuid();

        await using var context = _fixture.CreateContext();
        var sut = new CreateProductVariant(context);

        var created = await sut.ExecuteAsync(
            unknownProductId, region: null, edition: null, price: 100m, discountPercentage: null, currency: "ARS", sortOrder: 0, isActive: true);

        Assert.Null(created);

        await using var verify = _fixture.CreateContext();
        Assert.DoesNotContain(verify.ProductVariants, v => v.ProductId == unknownProductId);
    }

    /// <summary>Admin Variant Creation — "Invalid invariant rejected".</summary>
    [Fact]
    public async Task Invalid_price_throws_domain_exception_and_creates_nothing()
    {
        Guid productId;
        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(seed, CatalogTestData.UniquePlatform(), isActive: true);
            productId = product.Id;
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new CreateProductVariant(context);

        await Assert.ThrowsAsync<DomainException>(
            () => sut.ExecuteAsync(productId, region: null, edition: null, price: 0m, discountPercentage: null, currency: "ARS", sortOrder: 0, isActive: true));

        await using var verify = _fixture.CreateContext();
        Assert.DoesNotContain(verify.ProductVariants, v => v.ProductId == productId);
    }
}
