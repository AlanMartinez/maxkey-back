using Maxkeys.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Guides;

/// <summary>Public lookup for the storefront `/article/{slug}` page. One class per use case (ADR-02).</summary>
public sealed class GetGuideBySlug
{
    private readonly IAppDbContext _db;

    public GetGuideBySlug(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<GuideDto?> ExecuteAsync(string slug, CancellationToken cancellationToken = default) =>
        await _db.ActivationGuides
            .Where(g => g.Slug == slug)
            .Select(g => new GuideDto(g.Id, g.Slug, g.Title, g.ContentMarkdown))
            .SingleOrDefaultAsync(cancellationToken);
}
