using Maxkeys.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Guides;

/// <summary>Lists every activation guide for the admin picker (activation-guides spec). One class per use case (ADR-02).</summary>
public sealed class ListGuides
{
    private readonly IAppDbContext _db;

    public ListGuides(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<GuideDto>> ExecuteAsync(CancellationToken cancellationToken = default) =>
        await _db.ActivationGuides
            .OrderBy(g => g.Title)
            .Select(g => new GuideDto(g.Id, g.Slug, g.Title, g.ContentMarkdown))
            .ToListAsync(cancellationToken);
}
