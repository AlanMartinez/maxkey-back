using Maxkeys.Application.Persistence;
using Maxkeys.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Guides;

/// <summary>Updates an activation guide's slug/title/content. Returns null for an unknown id (→ 404). Throws <see cref="DomainConflictException"/> (409) if the slug is taken by another guide. One class per use case (ADR-02).</summary>
public sealed class UpdateGuide
{
    private readonly IAppDbContext _db;

    public UpdateGuide(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<GuideDto?> ExecuteAsync(Guid id, string slug, string title, string? contentMarkdown, CancellationToken cancellationToken = default)
    {
        var guide = await _db.ActivationGuides.SingleOrDefaultAsync(g => g.Id == id, cancellationToken);
        if (guide is null)
        {
            return null;
        }

        var slugTaken = await _db.ActivationGuides.AnyAsync(g => g.Id != id && g.Slug == slug, cancellationToken);
        if (slugTaken)
        {
            throw new DomainConflictException($"A guide with slug '{slug}' already exists.");
        }

        guide.Update(slug, title, contentMarkdown);
        await _db.SaveChangesAsync(cancellationToken);

        return new GuideDto(guide.Id, guide.Slug, guide.Title, guide.ContentMarkdown);
    }
}
