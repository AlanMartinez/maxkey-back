using Maxkeys.Application.Catalog;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Catalog;
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

    /// <summary>Admin Product Soft-Delete — "Product soft-deleted", every sibling field preserved.</summary>
    [Fact]
    public async Task Soft_deletes_a_product_preserving_sibling_fields()
    {
        Guid productId;
        await using (var seed = _fixture.CreateContext())
        {
            var product = new Product(
                $"product-{Guid.NewGuid():N}",
                "Keep Name",
                CatalogTestData.UniquePlatform(),
                isActive: true,
                imageKey: "products/keep.png",
                description: "Keep description",
                detailImageKey: "products/keep-detail.png",
                activationGuide: "**Kept.** This guide survives deletion.",
                activationType: "Keep activation");
            seed.Products.Add(product);
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
        Assert.Equal("products/keep-detail.png", deleted.DetailImageKey);
        Assert.Equal("Keep description", deleted.Description);
        Assert.Equal("**Kept.** This guide survives deletion.", deleted.ActivationGuide);
        Assert.Equal("Keep activation", deleted.ActivationType);

        await using var verify = _fixture.CreateContext();
        var reloaded = await verify.Products.FindAsync(productId);
        Assert.NotNull(reloaded);
        Assert.False(reloaded!.IsActive);
        Assert.Equal("Keep Name", reloaded.Name);
        Assert.Equal("products/keep-detail.png", reloaded.DetailImageKey);
        Assert.Equal("**Kept.** This guide survives deletion.", reloaded.ActivationGuide);
        Assert.Equal("Keep activation", reloaded.ActivationType);
    }

    /// <summary>Admin Product Soft-Delete — the returned record carries variants and gallery images in sort order.</summary>
    [Fact]
    public async Task Returns_variants_and_gallery_images_ordered_by_sort_order()
    {
        Guid productId;
        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(seed, CatalogTestData.UniquePlatform(), isActive: true);
            productId = product.Id;
            CatalogTestData.SeedVariant(seed, productId, price: 200m, sortOrder: 2, region: "US");
            CatalogTestData.SeedVariant(seed, productId, price: 100m, sortOrder: 1, region: "AR");
            seed.ProductImages.Add(new ProductImage(productId, "products/gallery/second.png", 1));
            seed.ProductImages.Add(new ProductImage(productId, "products/gallery/first.png", 0));
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new DeleteProduct(context, _imageUrlBuilder);

        var deleted = await sut.ExecuteAsync(productId);

        Assert.NotNull(deleted);
        Assert.Equal(["AR", "US"], deleted!.Variants.Select(v => v.Region));
        Assert.Equal(["products/gallery/first.png", "products/gallery/second.png"], deleted.ImageKeys);
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
