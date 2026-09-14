using Maxkeys.Application.Catalog;
using Maxkeys.Application.Persistence;
using Maxkeys.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Carousel;

/// <summary>
/// Updates a slide's product link, ordering, activation, and overrides (carousel
/// spec "Admin Slide Update and Removal"). Returns <see langword="null"/> for an
/// unknown slide id so the endpoint can map it to 404. Throws
/// <see cref="DomainException"/> — mapped to 422 — when <c>productId</c> does not
/// match any product, matching <see cref="CreateCarouselSlide"/>. One class per use
/// case (ADR-02).
/// </summary>
public sealed class UpdateCarouselSlide
{
    private readonly IAppDbContext _db;
    private readonly ImageUrlBuilder _imageUrlBuilder;

    public UpdateCarouselSlide(IAppDbContext db, ImageUrlBuilder imageUrlBuilder)
    {
        _db = db;
        _imageUrlBuilder = imageUrlBuilder;
    }

    public async Task<AdminCarouselSlide?> ExecuteAsync(
        Guid id,
        Guid productId,
        int sortOrder,
        bool isActive,
        string? title,
        string? caption,
        string? imageKey,
        CancellationToken cancellationToken = default)
    {
        var slide = await _db.CarouselSlides.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (slide is null)
        {
            return null;
        }

        var product = await _db.Products.SingleOrDefaultAsync(p => p.Id == productId, cancellationToken);
        if (product is null)
        {
            throw new DomainException("Carousel slide must reference an existing product.");
        }

        slide.Update(productId, sortOrder, isActive, title, caption, imageKey);
        await _db.SaveChangesAsync(cancellationToken);

        return ListCarouselSlides.ToAdminCarouselSlide(slide, product, _imageUrlBuilder);
    }
}
