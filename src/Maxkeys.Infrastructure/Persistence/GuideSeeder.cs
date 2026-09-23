using System.Text.Json;
using System.Text.Json.Serialization;
using Maxkeys.Domain.Guides;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Infrastructure.Persistence;

/// <summary>
/// Upserts operator-maintained activation guides from a JSON seed file, matched by
/// <see cref="ActivationGuide.Slug"/> (mirrors <see cref="CatalogSeeder"/>'s idempotent
/// upsert-by-slug pattern). Wired via <c>Maxkeys.Api --seed-guides &lt;path&gt;</c> (<c>Program.cs</c>).
/// Ships one example guide (<c>seed/guides.json</c>) for the admin to edit and clone from.
/// </summary>
public static class GuideSeeder
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task SeedAsync(AppDbContext db, string jsonFilePath, CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(jsonFilePath, cancellationToken);
        var document = JsonSerializer.Deserialize<GuideSeedDocument>(json, JsonOptions) ?? new GuideSeedDocument([]);

        foreach (var seedGuide in document.Guides)
        {
            var guide = await db.ActivationGuides.SingleOrDefaultAsync(g => g.Slug == seedGuide.Slug, cancellationToken);

            if (guide is null)
            {
                db.ActivationGuides.Add(new ActivationGuide(seedGuide.Slug, seedGuide.Title, seedGuide.ContentMarkdown));
            }
            else
            {
                guide.Update(seedGuide.Slug, seedGuide.Title, seedGuide.ContentMarkdown);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

internal sealed record GuideSeedDocument(List<GuideSeedItem> Guides);

internal sealed record GuideSeedItem(
    string Slug,
    string Title,
    [property: JsonPropertyName("contentMarkdown")] string? ContentMarkdown);
