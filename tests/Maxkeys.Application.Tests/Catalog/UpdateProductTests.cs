using Maxkeys.Application.Catalog;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Common;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Tests.Catalog;

[Collection(PostgresCollection.Name)]
public sealed class UpdateProductTests
{
    private readonly PostgresFixture _fixture;
    private readonly ImageUrlBuilder _imageUrlBuilder;

    public UpdateProductTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _imageUrlBuilder = new ImageUrlBuilder(
            Options.Create(new StorageOptions { R2PublicBaseUrl = "https://img.test" }));
    }

    [Fact]
    public async Task Updates_description_and_image_key()
    {
        Guid productId;
        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(seed, CatalogTestData.UniquePlatform(), isActive: true, imageKey: "products/old.png");
            productId = product.Id;
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new UpdateProduct(context, _imageUrlBuilder);

        var updated = await sut.ExecuteAsync(
            productId, "New Name", "PSN", "New description", "products/new.png", isActive: true);

        Assert.NotNull(updated);
        Assert.Equal("New Name", updated!.Name);
        Assert.Equal("New description", updated.Description);
        Assert.Equal("products/new.png", updated.ImageKey);
    }

    [Fact]
    public async Task Empty_required_field_throws_domain_exception_and_leaves_product_unchanged()
    {
        Guid productId;
        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(seed, CatalogTestData.UniquePlatform(), isActive: true, name: "Original Name");
            productId = product.Id;
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new UpdateProduct(context, _imageUrlBuilder);

        await Assert.ThrowsAsync<DomainException>(
            () => sut.ExecuteAsync(productId, string.Empty, "PSN", null, null, isActive: true));

        await using var verify = _fixture.CreateContext();
        var reloaded = await verify.Products.FindAsync(productId);
        Assert.Equal("Original Name", reloaded!.Name);
    }

    [Fact]
    public async Task Returns_null_for_unknown_id()
    {
        await using var context = _fixture.CreateContext();
        var sut = new UpdateProduct(context, _imageUrlBuilder);

        var updated = await sut.ExecuteAsync(Guid.NewGuid(), "Name", "PSN", null, null, isActive: true);

        Assert.Null(updated);
    }

    /// <summary>Admin Product Activation Toggle — deactivation persists `IsActive = false`.</summary>
    [Fact]
    public async Task Deactivating_a_product_persists_is_active_false()
    {
        Guid productId;
        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(seed, CatalogTestData.UniquePlatform(), isActive: true);
            productId = product.Id;
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new UpdateProduct(context, _imageUrlBuilder);

        var updated = await sut.ExecuteAsync(productId, "Name", "PSN", null, null, isActive: false);

        Assert.NotNull(updated);
        Assert.False(updated!.IsActive);

        await using var verify = _fixture.CreateContext();
        var reloaded = await verify.Products.FindAsync(productId);
        Assert.False(reloaded!.IsActive);
    }
}
