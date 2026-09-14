using Maxkeys.Application.Catalog;
using Maxkeys.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Carousel;

/// <summary>
/// Lists every carousel slide, including inactive ones and slides linked to an
/// inactive product, for the admin view (design contract table,
/// <c>GET /admin/carousel</c>). Ordered by <c>SortOrder</c> then <c>Id</c>, matching
/// <see cref="GetCarousel"/>. One class per use case (ADR-02).
/// </summary>
public sealed class ListCarouselSlides
{
    private readonly IAppDbContext _db;
    private readonly ImageUrlBuilder _imageUrlBuilder;

    public ListCarouselSlides(IAppDbContext db, ImageUrlBuilder imageUrlBuilder)
    {
        _db = db;
        _imageUrlBuilder = imageUrlBuilder;
    }

    public async Task<IReadOnlyList<AdminCarouselSlide>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var rows = await (
            from slide in _db.CarouselSlides
            join product in _db.Products on slide.ProductId equals product.Id
            orderby slide.SortOrder, slide.Id
            select new { slide, product })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => ToAdminCarouselSlide(row.slide, row.product, _imageUrlBuilder))
            .ToList();
    }

    internal static AdminCarouselSlide ToAdminCarouselSlide(
        Domain.Carousel.CarouselSlide slide, Domain.Catalog.Product product, ImageUrlBuilder imageUrlBuilder) =>
        new(
            slide.Id,
            product.Id,
            product.Name,
            product.Slug,
            product.IsActive,
            slide.SortOrder,
            slide.IsActive,
            slide.Title,
            slide.Caption,
            slide.ImageKey,
            imageUrlBuilder.Build(slide.ImageKey ?? product.ImageKey));
}
