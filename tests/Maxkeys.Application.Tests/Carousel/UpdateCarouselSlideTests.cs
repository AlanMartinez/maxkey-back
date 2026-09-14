using Maxkeys.Application.Carousel;
using Maxkeys.Application.Catalog;
using Maxkeys.Application.Tests.Catalog;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Common;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Tests.Carousel;

[Collection(PostgresCollection.Name)]
public sealed class UpdateCarouselSlideTests
{
    private readonly PostgresFixture _fixture;
    private readonly ImageUrlBuilder _imageUrlBuilder;

    public UpdateCarouselSlideTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _imageUrlBuilder = new ImageUrlBuilder(
            Options.Create(new StorageOptions { R2PublicBaseUrl = "https://img.test" }));
    }

    /// <summary>Admin Slide Update and Removal — "Slide reordered".</summary>
    [Fact]
    public async Task Reorders_a_slide()
    {
        Guid productId, slideId;
        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(seed, CatalogTestData.UniquePlatform(), isActive: true);
            productId = product.Id;
            var slide = CarouselTestData.SeedSlide(seed, productId, sortOrder: 2);
            slideId = slide.Id;
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new UpdateCarouselSlide(context, _imageUrlBuilder);

        var updated = await sut.ExecuteAsync(slideId, productId, sortOrder: 0, isActive: true, title: null, caption: null, imageKey: null);

        Assert.NotNull(updated);
        Assert.Equal(0, updated!.SortOrder);
    }

    [Fact]
    public async Task Rejects_unknown_product_with_domain_exception()
    {
        Guid productId, slideId;
        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(seed, CatalogTestData.UniquePlatform(), isActive: true);
            productId = product.Id;
            var slide = CarouselTestData.SeedSlide(seed, productId, sortOrder: 0);
            slideId = slide.Id;
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new UpdateCarouselSlide(context, _imageUrlBuilder);

        await Assert.ThrowsAsync<DomainException>(
            () => sut.ExecuteAsync(slideId, Guid.NewGuid(), sortOrder: 0, isActive: true, title: null, caption: null, imageKey: null));
    }

    [Fact]
    public async Task Returns_null_for_unknown_slide_id()
    {
        Guid productId;
        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(seed, CatalogTestData.UniquePlatform(), isActive: true);
            productId = product.Id;
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new UpdateCarouselSlide(context, _imageUrlBuilder);

        var updated = await sut.ExecuteAsync(
            Guid.NewGuid(), productId, sortOrder: 0, isActive: true, title: null, caption: null, imageKey: null);

        Assert.Null(updated);
    }
}
