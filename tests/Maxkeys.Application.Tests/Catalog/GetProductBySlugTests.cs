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
            CatalogTestData.SeedVariant(seed, product.Id, price: 100m, discountPercentage: 20m, sortOrder: 0, region: "AR", edition: "Standard");
            CatalogTestData.SeedVariant(seed, product.Id, price: 999m, sortOrder: 2, isActive: false);
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new GetProductBySlug(context, _imageUrlBuilder);

        var detail = await sut.ExecuteAsync(slug);

        Assert.NotNull(detail);
        Assert.Equal(2, detail!.Variants.Count);
        Assert.Equal(100m, detail.FromPrice);
        Assert.Equal(125m, detail.OldPrice);
        Assert.Equal("AR · Standard", detail.Variants[0].Name);
        Assert.Equal("AR · Deluxe", detail.Variants[1].Name);
    }

    [Fact]
    public async Task Returns_description_from_the_product_entity()
    {
        var slug = $"slug-{Guid.NewGuid():N}";

        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(
                seed,
                CatalogTestData.UniquePlatform(),
                isActive: true,
                slug: slug,
                description: "500 ARS worth of in-game currency.");
            CatalogTestData.SeedVariant(seed, product.Id, price: 100m);
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new GetProductBySlug(context, _imageUrlBuilder);

        var detail = await sut.ExecuteAsync(slug);

        Assert.NotNull(detail);
        Assert.Equal("500 ARS worth of in-game currency.", detail!.Description);
    }

    [Fact]
    public async Task Returns_gallery_images_ordered_and_activation_fields()
    {
        var slug = $"slug-{Guid.NewGuid():N}";

        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(seed, CatalogTestData.UniquePlatform(), isActive: true, slug: slug);
            CatalogTestData.SeedVariant(seed, product.Id, price: 100m);
            seed.ProductImages.Add(new Domain.Catalog.ProductImage(product.Id, "products/gallery/2.png", sortOrder: 1));
            seed.ProductImages.Add(new Domain.Catalog.ProductImage(product.Id, "products/gallery/1.png", sortOrder: 0));
            product.UpdateCatalogInfo(
                product.Name, product.Platform, product.Description, product.ImageKey, product.DetailImageKey, product.IsActive,
                activationGuide: "**Step 1.** Open the launcher and redeem the key.", activationType: "Enlace de activación");
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new GetProductBySlug(context, _imageUrlBuilder);

        var detail = await sut.ExecuteAsync(slug);

        Assert.NotNull(detail);
        Assert.Equal(["https://img.test/products/gallery/1.png", "https://img.test/products/gallery/2.png"], detail!.Images);
        Assert.Equal("**Step 1.** Open the launcher and redeem the key.", detail.ActivationGuide);
        Assert.Equal("Enlace de activación", detail.ActivationType);
    }

    [Fact]
    public async Task Prepends_the_catalog_image_before_gallery_images()
    {
        var slug = $"slug-{Guid.NewGuid():N}";

        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(
                seed, CatalogTestData.UniquePlatform(), isActive: true, slug: slug, imageKey: "products/main.png");
            CatalogTestData.SeedVariant(seed, product.Id, price: 100m);
            seed.ProductImages.Add(new Domain.Catalog.ProductImage(product.Id, "products/gallery/1.png", sortOrder: 0));
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new GetProductBySlug(context, _imageUrlBuilder);

        var detail = await sut.ExecuteAsync(slug);

        Assert.NotNull(detail);
        Assert.Equal(["https://img.test/products/main.png", "https://img.test/products/gallery/1.png"], detail!.Images);
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

    [Fact]
    public async Task FromPrice_uses_the_recommended_variant_over_a_cheaper_one_and_maps_the_flag()
    {
        var slug = $"slug-{Guid.NewGuid():N}";

        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(seed, CatalogTestData.UniquePlatform(), isActive: true, slug: slug);
            CatalogTestData.SeedVariant(seed, product.Id, price: 100m, sortOrder: 0);
            CatalogTestData.SeedVariant(seed, product.Id, price: 200m, discountPercentage: 20m, sortOrder: 1, isRecommended: true);
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new GetProductBySlug(context, _imageUrlBuilder);

        var detail = await sut.ExecuteAsync(slug);

        Assert.NotNull(detail);
        Assert.Equal(200m, detail!.FromPrice);
        Assert.Equal(250m, detail.OldPrice);
        Assert.False(detail.Variants[0].IsRecommended);
        Assert.True(detail.Variants[1].IsRecommended);
    }

    [Fact]
    public async Task FromPrice_falls_back_to_the_cheapest_variant_when_none_is_recommended()
    {
        var slug = $"slug-{Guid.NewGuid():N}";

        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(seed, CatalogTestData.UniquePlatform(), isActive: true, slug: slug);
            CatalogTestData.SeedVariant(seed, product.Id, price: 300m, sortOrder: 0);
            CatalogTestData.SeedVariant(seed, product.Id, price: 100m, sortOrder: 1);
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new GetProductBySlug(context, _imageUrlBuilder);

        var detail = await sut.ExecuteAsync(slug);

        Assert.NotNull(detail);
        Assert.Equal(100m, detail!.FromPrice);
        Assert.All(detail.Variants, v => Assert.False(v.IsRecommended));
    }

    [Fact]
    public async Task FromPrice_falls_back_to_the_cheapest_variant_when_the_recommended_one_is_inactive()
    {
        var slug = $"slug-{Guid.NewGuid():N}";

        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(seed, CatalogTestData.UniquePlatform(), isActive: true, slug: slug);
            CatalogTestData.SeedVariant(seed, product.Id, price: 300m, sortOrder: 0);
            CatalogTestData.SeedVariant(seed, product.Id, price: 100m, sortOrder: 1);
            CatalogTestData.SeedVariant(seed, product.Id, price: 500m, sortOrder: 2, isActive: false, isRecommended: true);
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new GetProductBySlug(context, _imageUrlBuilder);

        var detail = await sut.ExecuteAsync(slug);

        Assert.NotNull(detail);
        Assert.Equal(2, detail!.Variants.Count);
        Assert.Equal(100m, detail.FromPrice);
    }
}
