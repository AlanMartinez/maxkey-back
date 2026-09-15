using Maxkeys.Application.Catalog;
using Maxkeys.Application.Tests.Fixtures;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Tests.Catalog;

[Collection(PostgresCollection.Name)]
public sealed class DeleteProductTests
{
    private readonly PostgresFixture _fixture;
    private readonly ImageUrlBuilder _imageUrlBuilder;

    public DeleteProductTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _imageUrlBuilder = new ImageUrlBuilder(
            Options.Create(new StorageOptions { R2PublicBaseUrl = "https://img.test" }));
    }

    /// <summary>Admin Product Soft-Delete — "Product soft-deleted", sibling fields preserved.</summary>
    [Fact]
    public async Task Soft_deletes_a_product_preserving_sibling_fields()
    {
        Guid productId;
        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(
                seed, CatalogTestData.UniquePlatform(), isActive: true, name: "Keep Name", imageKey: "products/keep.png", description: "Keep description");
            productId = product.Id;
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new DeleteProduct(context, _imageUrlBuilder);

        var deleted = await sut.ExecuteAsync(productId);

        Assert.NotNull(deleted);
        Assert.False(deleted!.IsActive);
        Assert.Equal("Keep Name", deleted.Name);
        Assert.Equal("products/keep.png", deleted.ImageKey);
        Assert.Equal("Keep description", deleted.Description);

        await using var verify = _fixture.CreateContext();
        var reloaded = await verify.Products.FindAsync(productId);
        Assert.NotNull(reloaded);
        Assert.False(reloaded!.IsActive);
        Assert.Equal("Keep Name", reloaded.Name);
    }

    /// <summary>Admin Product Soft-Delete — "Product not found".</summary>
    [Fact]
    public async Task Returns_null_for_unknown_id()
    {
        await using var context = _fixture.CreateContext();
        var sut = new DeleteProduct(context, _imageUrlBuilder);

        var deleted = await sut.ExecuteAsync(Guid.NewGuid());

        Assert.Null(deleted);
    }
}
