using Maxkeys.Application.Persistence;
using Maxkeys.Domain.Common;
using Maxkeys.Domain.Guides;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Guides;

/// <summary>Creates a new activation guide. Throws <see cref="DomainConflictException"/> (409) for a duplicate slug. One class per use case (ADR-02).</summary>
public sealed class CreateGuide
{
    private readonly IAppDbContext _db;

    public CreateGuide(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<GuideDto> ExecuteAsync(string slug, string title, string? contentMarkdown, CancellationToken cancellationToken = default)
    {
        var slugTaken = await _db.ActivationGuides.AnyAsync(g => g.Slug == slug, cancellationToken);
        if (slugTaken)
        {
            throw new DomainConflictException($"A guide with slug '{slug}' already exists.");
        }

        var guide = new ActivationGuide(slug, title, contentMarkdown);
        _db.ActivationGuides.Add(guide);
        await _db.SaveChangesAsync(cancellationToken);

        return new GuideDto(guide.Id, guide.Slug, guide.Title, guide.ContentMarkdown);
    }
}
