using System.Net.Http.Json;
using Maxkeys.Api.Tests.Fixtures;
using Maxkeys.Application.Carousel;
using Maxkeys.Domain.Carousel;
using Maxkeys.Domain.Catalog;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Maxkeys.Api.Tests.Catalog;

/// <summary>Covers the carousel spec: Public Carousel Listing (anonymous access, ordering, hiding).</summary>
[Collection(ApiCollection.Name)]
public sealed class CarouselEndpointTests
{
    private readonly ApiTestFixture _factory;

    public CarouselEndpointTests(ApiTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Anonymous_request_returns_ordered_active_slides()
    {
        var platform = UniquePlatform();
        Guid firstId = default, secondId = default;
        await Seed(async db =>
        {
            var productA = new Product($"a-{Guid.NewGuid():N}", "Product A", platform);
            var productB = new Product($"b-{Guid.NewGuid():N}", "Product B", platform);
            db.Products.AddRange(productA, productB);
            var second = new CarouselSlide(productB.Id, sortOrder: 2);
            var first = new CarouselSlide(productA.Id, sortOrder: 1);
            db.CarouselSlides.AddRange(first, second);
            await db.SaveChangesAsync();
            firstId = first.Id;
            secondId = second.Id;
        });

        var client = _factory.CreateClient();
        var slides = await client.GetFromJsonAsync<List<CarouselSlideSummary>>("/catalog/carousel");

        var ordered = slides!.Where(s => s.Id == firstId || s.Id == secondId).ToList();
        Assert.Equal(2, ordered.Count);
        Assert.True(ordered[0].SortOrder < ordered[1].SortOrder);
    }

    [Fact]
    public async Task Hides_inactive_slides_and_slides_for_inactive_products()
    {
        var platform = UniquePlatform();
        Guid inactiveSlideId = default, slideForInactiveProductId = default;
        await Seed(async db =>
        {
            var activeProduct = new Product($"active-{Guid.NewGuid():N}", "Active", platform);
            var inactiveProduct = new Product($"inactive-{Guid.NewGuid():N}", "Inactive", platform, isActive: false);
            db.Products.AddRange(activeProduct, inactiveProduct);
            var inactiveSlide = new CarouselSlide(activeProduct.Id, sortOrder: 0, isActive: false);
            var slideForInactiveProduct = new CarouselSlide(inactiveProduct.Id, sortOrder: 1, isActive: true);
            db.CarouselSlides.AddRange(inactiveSlide, slideForInactiveProduct);
            await db.SaveChangesAsync();
            inactiveSlideId = inactiveSlide.Id;
            slideForInactiveProductId = slideForInactiveProduct.Id;
        });

        var client = _factory.CreateClient();
        var slides = await client.GetFromJsonAsync<List<CarouselSlideSummary>>("/catalog/carousel");

        Assert.DoesNotContain(slides!, s => s.Id == inactiveSlideId);
        Assert.DoesNotContain(slides!, s => s.Id == slideForInactiveProductId);
    }

    private static string UniquePlatform() => $"platform-{Guid.NewGuid():N}";

    private async Task Seed(Func<AppDbContext, Task> seed)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await seed(db);
    }
}
