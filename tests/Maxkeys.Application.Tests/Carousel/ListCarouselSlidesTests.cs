using Maxkeys.Application.Carousel;
using Maxkeys.Application.Catalog;
using Maxkeys.Application.Tests.Catalog;
using Maxkeys.Application.Tests.Fixtures;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Tests.Carousel;

[Collection(PostgresCollection.Name)]
public sealed class ListCarouselSlidesTests
{
    private readonly PostgresFixture _fixture;
    private readonly ImageUrlBuilder _imageUrlBuilder;

    public ListCarouselSlidesTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _imageUrlBuilder = new ImageUrlBuilder(
            Options.Create(new StorageOptions { R2PublicBaseUrl = "https://img.test" }));
    }

    [Fact]
    public async Task Includes_inactive_slides_and_slides_linked_to_inactive_products()
    {
        var platform = CatalogTestData.UniquePlatform();
        Guid inactiveSlideId, slideForInactiveProductId;
        await using (var seed = _fixture.CreateContext())
        {
            var activeProduct = CatalogTestData.SeedProduct(seed, platform, isActive: true);
            var inactiveProduct = CatalogTestData.SeedProduct(seed, platform, isActive: false);
            var inactiveSlide = CarouselTestData.SeedSlide(seed, activeProduct.Id, sortOrder: 0, isActive: false);
            var slideForInactiveProduct = CarouselTestData.SeedSlide(seed, inactiveProduct.Id, sortOrder: 1, isActive: true);
            await seed.SaveChangesAsync();
            inactiveSlideId = inactiveSlide.Id;
            slideForInactiveProductId = slideForInactiveProduct.Id;
        }

        await using var context = _fixture.CreateContext();
        var sut = new ListCarouselSlides(context, _imageUrlBuilder);

        var result = await sut.ExecuteAsync();

        Assert.Contains(result, s => s.Id == inactiveSlideId && !s.IsActive);
        Assert.Contains(result, s => s.Id == slideForInactiveProductId && !s.ProductIsActive);
    }
}
