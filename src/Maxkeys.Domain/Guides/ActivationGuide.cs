using Maxkeys.Domain.Common;

namespace Maxkeys.Domain.Guides;

/// <summary>
/// A reusable, admin-authored activation guide page (activation-guides spec).
/// Rendered standalone at <c>/article/{Slug}</c> on the storefront and referenced
/// by zero or more <see cref="Domain.Catalog.Product"/> rows via
/// <c>Product.ActivationGuideId</c> — the same guide can be linked from many
/// products (e.g. every Microsoft gift card), so there is no back-reference here.
/// Unlike <c>Product</c>, there is no seeder upsert-by-slug path pinning the slug,
/// so <see cref="Update"/> is free to change it too.
/// </summary>
public sealed class ActivationGuide : Entity
{
    public string Slug { get; private set; }
    public string Title { get; private set; }
    public string ContentMarkdown { get; private set; }

    public ActivationGuide(string slug, string title, string? contentMarkdown = null)
    {
        ValidateSlug(slug);
        ValidateTitle(title);

        Slug = slug;
        Title = title;
        ContentMarkdown = contentMarkdown ?? string.Empty;
    }

    public void Update(string slug, string title, string? contentMarkdown)
    {
        ValidateSlug(slug);
        ValidateTitle(title);

        Slug = slug;
        Title = title;
        ContentMarkdown = contentMarkdown ?? string.Empty;
    }

    private static void ValidateSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new DomainException("Guide slug must not be empty.");
        }

        if (slug != slug.ToLowerInvariant())
        {
            throw new DomainException("Guide slug must be lowercase.");
        }
    }

    private static void ValidateTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("Guide title must not be empty.");
        }
    }
}
