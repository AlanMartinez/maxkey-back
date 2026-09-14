using Maxkeys.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Carousel;

/// <summary>
/// Deletes a carousel slide (carousel spec "Admin Slide Update and Removal" —
/// "Slide deleted"). Returns <see langword="false"/> for an unknown id so the
/// endpoint can map it to 404. One class per use case (ADR-02).
/// </summary>
public sealed class DeleteCarouselSlide
{
    private readonly IAppDbContext _db;

    public DeleteCarouselSlide(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<bool> ExecuteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var slide = await _db.CarouselSlides.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (slide is null)
        {
            return false;
        }

        _db.CarouselSlides.Remove(slide);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
