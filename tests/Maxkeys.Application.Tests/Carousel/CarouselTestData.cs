using Maxkeys.Domain.Carousel;
using Maxkeys.Infrastructure.Persistence;

namespace Maxkeys.Application.Tests.Carousel;

/// <summary>Shared seed helper for carousel use-case tests.</summary>
internal static class CarouselTestData
{
    public static CarouselSlide SeedSlide(
        AppDbContext context,
        Guid productId,
        int sortOrder = 0,
        bool isActive = true,
        string? title = null,
        string? caption = null,
        string? imageKey = null)
    {
        var slide = new CarouselSlide(productId, sortOrder, isActive, title, caption, imageKey);
        context.CarouselSlides.Add(slide);
        return slide;
    }
}
