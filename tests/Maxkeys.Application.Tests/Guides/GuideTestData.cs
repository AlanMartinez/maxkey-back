using Maxkeys.Domain.Guides;
using Maxkeys.Infrastructure.Persistence;

namespace Maxkeys.Application.Tests.Guides;

internal static class GuideTestData
{
    public static string UniqueSlug() => $"guide-{Guid.NewGuid():N}";

    public static ActivationGuide SeedGuide(AppDbContext context, string? slug = null, string title = "Guide", string contentMarkdown = "content")
    {
        var guide = new ActivationGuide(slug ?? UniqueSlug(), title, contentMarkdown);
        context.ActivationGuides.Add(guide);
        return guide;
    }
}
