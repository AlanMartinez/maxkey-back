using Maxkeys.Application.Carousel;
using Maxkeys.Application.Catalog;
using Maxkeys.Application.Tests.Catalog;
using Maxkeys.Application.Tests.Fixtures;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Tests.Carousel;

[Collection(PostgresCollection.Name)]
public sealed class GetCarouselTests
{
    private readonly PostgresFixture _fixture;
    private readonly ImageUrlBuilder _imageUrlBuilder;

    public GetCarouselTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _imageUrlBuilder = new ImageUrlBuilder(
            Options.Create(new StorageOptions { R2PublicBaseUrl = "https://img.test" }));
    }

    /// <summary>Public Carousel Listing — "Inactive slide hidden".</summary>
    [Fact]
    public async Task Hides_inactive_slide()
    {
        var platform = CatalogTestData.UniquePlatform();
        string activeSlug;
        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(seed, platform, isActive: true);
            activeSlug = product.Slug;
            CarouselTestData.SeedSlide(seed, product.Id, sortOrder: 0, isActive: false);
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new GetCarousel(context, _imageUrlBuilder);

        var result = await sut.ExecuteAsync();

        Assert.DoesNotContain(result, s => s.ProductSlug == activeSlug);
    }

    /// <summary>Public Carousel Listing — "Slide hidden when its product is inactive".</summary>
    [Fact]
    public async Task Hides_slide_when_its_product_is_inactive()
    {
        var platform = CatalogTestData.UniquePlatform();
        string slug;
        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(seed, platform, isActive: false);
            slug = product.Slug;
            CarouselTestData.SeedSlide(seed, product.Id, sortOrder: 0, isActive: true);
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new GetCarousel(context, _imageUrlBuilder);

        var result = await sut.ExecuteAsync();

        Assert.DoesNotContain(result, s => s.ProductSlug == slug);
    }

    /// <summary>Public Carousel Listing — "Override falls back to product fields".</summary>
    [Fact]
    public async Task Falls_back_to_product_title_and_image_when_no_override()
    {
        var platform = CatalogTestData.UniquePlatform();
        Guid productId;
        string productName;
        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(seed, platform, isActive: true, name: "FC Points", imageKey: "products/fc.png");
            productId = product.Id;
            productName = product.Name;
            CarouselTestData.SeedSlide(seed, productId, sortOrder: 0, isActive: true);
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new GetCarousel(context, _imageUrlBuilder);

        var result = await sut.ExecuteAsync();

        var slide = Assert.Single(result, s => s.Title == productName);
        Assert.Equal("https://img.test/products/fc.png", slide.ImageUrl);
    }

    [Fact]
    public async Task Uses_slide_override_instead_of_product_fields()
    {
        var platform = CatalogTestData.UniquePlatform();
        Guid productId;
        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(seed, platform, isActive: true, name: "Product Name", imageKey: "products/product.png");
            productId = product.Id;
            CarouselTestData.SeedSlide(
                seed, productId, sortOrder: 0, isActive: true, title: "Custom Title", caption: "Custom Caption", imageKey: "products/override.png");
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new GetCarousel(context, _imageUrlBuilder);

        var result = await sut.ExecuteAsync();

        var slide = Assert.Single(result, s => s.Title == "Custom Title");
        Assert.Equal("Custom Caption", slide.Caption);
        Assert.Equal("https://img.test/products/override.png", slide.ImageUrl);
    }

    [Fact]
    public async Task Orders_by_sort_order_ascending()
    {
        var platform = CatalogTestData.UniquePlatform();
        Guid second, first;
        await using (var seed = _fixture.CreateContext())
        {
            var productA = CatalogTestData.SeedProduct(seed, platform, isActive: true);
            var productB = CatalogTestData.SeedProduct(seed, platform, isActive: true);
            var slideB = CarouselTestData.SeedSlide(seed, productB.Id, sortOrder: 2);
            var slideA = CarouselTestData.SeedSlide(seed, productA.Id, sortOrder: 1);
            await seed.SaveChangesAsync();
            first = slideA.Id;
            second = slideB.Id;
        }

        await using var context = _fixture.CreateContext();
        var sut = new GetCarousel(context, _imageUrlBuilder);

        var result = (await sut.ExecuteAsync()).Where(s => s.Id == first || s.Id == second).ToList();

        Assert.Equal(first, result[0].Id);
        Assert.Equal(second, result[1].Id);
    }
}
