using Maxkeys.Application.Catalog;
using Maxkeys.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Carousel;

/// <summary>
/// Lists carousel slides for the public storefront (carousel spec "Public Carousel
/// Listing"). Excludes slides where <c>IsActive=false</c> or the linked product is
/// inactive, ordered by <c>SortOrder</c> then <c>Id</c> for a stable tie-break. Each
/// slide's <see cref="CarouselSlideSummary.Title"/>/<see cref="CarouselSlideSummary.Caption"/>/
/// image resolve the slide's override, falling back to the linked product's
/// Name/Description/ImageKey. One class per use case (ADR-02).
/// </summary>
public sealed class GetCarousel
{
    private readonly IAppDbContext _db;
    private readonly ImageUrlBuilder _imageUrlBuilder;

    public GetCarousel(IAppDbContext db, ImageUrlBuilder imageUrlBuilder)
    {
        _db = db;
        _imageUrlBuilder = imageUrlBuilder;
    }

    public async Task<IReadOnlyList<CarouselSlideSummary>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var rows = await (
            from slide in _db.CarouselSlides
            join product in _db.Products on slide.ProductId equals product.Id
            where slide.IsActive && product.IsActive
            orderby slide.SortOrder, slide.Id
            select new { slide, product })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new CarouselSlideSummary(
                row.slide.Id,
                row.slide.Title ?? row.product.Name,
                row.slide.Caption ?? row.product.Description,
                _imageUrlBuilder.Build(row.slide.ImageKey ?? row.product.ImageKey),
                row.product.Slug,
                row.slide.SortOrder))
            .ToList();
    }
}
