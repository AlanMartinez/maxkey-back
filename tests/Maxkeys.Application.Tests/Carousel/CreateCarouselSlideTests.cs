using Maxkeys.Application.Carousel;
using Maxkeys.Application.Catalog;
using Maxkeys.Application.Tests.Catalog;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Common;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Tests.Carousel;

[Collection(PostgresCollection.Name)]
public sealed class CreateCarouselSlideTests
{
    private readonly PostgresFixture _fixture;
    private readonly ImageUrlBuilder _imageUrlBuilder;

    public CreateCarouselSlideTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _imageUrlBuilder = new ImageUrlBuilder(
            Options.Create(new StorageOptions { R2PublicBaseUrl = "https://img.test" }));
    }

    /// <summary>Admin Slide Creation — "Slide created for an existing product".</summary>
    [Fact]
    public async Task Creates_slide_for_an_existing_product()
    {
        Guid productId;
        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(seed, CatalogTestData.UniquePlatform(), isActive: true);
            productId = product.Id;
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new CreateCarouselSlide(context, _imageUrlBuilder);

        var slide = await sut.ExecuteAsync(productId, sortOrder: 1, isActive: true, title: null, caption: null, imageKey: null);

        Assert.Equal(productId, slide.ProductId);
        Assert.Equal(1, slide.SortOrder);

        await using var verify = _fixture.CreateContext();
        Assert.NotNull(await verify.CarouselSlides.FindAsync(slide.Id));
    }

    /// <summary>Admin Slide Creation — "Slide creation rejects unknown product".</summary>
    [Fact]
    public async Task Rejects_unknown_product_with_domain_exception()
    {
        var unknownProductId = Guid.NewGuid();
        await using var context = _fixture.CreateContext();
        var sut = new CreateCarouselSlide(context, _imageUrlBuilder);

        await Assert.ThrowsAsync<DomainException>(
            () => sut.ExecuteAsync(unknownProductId, sortOrder: 0, isActive: true, title: null, caption: null, imageKey: null));

        await using var verify = _fixture.CreateContext();
        Assert.DoesNotContain(verify.CarouselSlides, s => s.ProductId == unknownProductId);
    }
}
