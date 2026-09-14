using Maxkeys.Application.Carousel;
using Maxkeys.Application.Catalog;
using Maxkeys.Application.Tests.Catalog;
using Maxkeys.Application.Tests.Fixtures;

namespace Maxkeys.Application.Tests.Carousel;

[Collection(PostgresCollection.Name)]
public sealed class DeleteCarouselSlideTests
{
    private readonly PostgresFixture _fixture;

    public DeleteCarouselSlideTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Admin Slide Update and Removal — "Slide deleted".</summary>
    [Fact]
    public async Task Deletes_an_existing_slide()
    {
        Guid slideId;
        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(seed, CatalogTestData.UniquePlatform(), isActive: true);
            var slide = CarouselTestData.SeedSlide(seed, product.Id, sortOrder: 0);
            slideId = slide.Id;
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new DeleteCarouselSlide(context);

        var deleted = await sut.ExecuteAsync(slideId);

        Assert.True(deleted);
        await using var verify = _fixture.CreateContext();
        Assert.Null(await verify.CarouselSlides.FindAsync(slideId));
    }

    [Fact]
    public async Task Returns_false_for_unknown_id()
    {
        await using var context = _fixture.CreateContext();
        var sut = new DeleteCarouselSlide(context);

        var deleted = await sut.ExecuteAsync(Guid.NewGuid());

        Assert.False(deleted);
    }
}
