using Maxkeys.Application.Catalog;
using Maxkeys.Application.Persistence;
using Maxkeys.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Carousel;

/// <summary>
/// Creates a carousel slide referencing an existing product (carousel spec "Admin
/// Slide Creation"). Throws <see cref="DomainException"/> — mapped to 422 by
/// <c>ProblemDetailsExceptionHandler</c> — when <c>productId</c> does not match any
/// product ("Slide creation rejects unknown product"). One class per use case (ADR-02).
/// </summary>
public sealed class CreateCarouselSlide
{
    private readonly IAppDbContext _db;
    private readonly ImageUrlBuilder _imageUrlBuilder;

    public CreateCarouselSlide(IAppDbContext db, ImageUrlBuilder imageUrlBuilder)
    {
        _db = db;
        _imageUrlBuilder = imageUrlBuilder;
    }

    public async Task<AdminCarouselSlide> ExecuteAsync(
        Guid productId,
        int sortOrder,
        bool isActive,
        string? title,
        string? caption,
        string? imageKey,
        CancellationToken cancellationToken = default)
    {
        var product = await _db.Products.SingleOrDefaultAsync(p => p.Id == productId, cancellationToken);
        if (product is null)
        {
            throw new DomainException("Carousel slide must reference an existing product.");
        }

        var slide = new Domain.Carousel.CarouselSlide(productId, sortOrder, isActive, title, caption, imageKey);
        _db.CarouselSlides.Add(slide);
        await _db.SaveChangesAsync(cancellationToken);

        return ListCarouselSlides.ToAdminCarouselSlide(slide, product, _imageUrlBuilder);
    }
}
