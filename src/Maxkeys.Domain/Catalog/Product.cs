using Maxkeys.Domain.Common;

namespace Maxkeys.Domain.Catalog;

/// <summary>
/// A sellable product (e.g. a gift card family). Variants (region/edition/tier)
/// are still added by the catalog seeder only — no admin variant creation.
/// Products themselves were seed-only through the MVP (design section 4.1);
/// <c>Maxkeys.Application.Catalog.CreateProduct</c> later added an admin path
/// that also constructs a <see cref="Product"/> directly. <see cref="UpdateCatalogInfo"/>
/// is the one mutation method, added in PR12 so <c>CatalogSeeder</c> (ADR-12)
/// can upsert an existing row by <see cref="Slug"/> instead of only inserting.
/// </summary>
public sealed class Product : Entity
{
    public string Slug { get; private set; }
    public string Name { get; private set; }
    public string Platform { get; private set; }
    public bool IsActive { get; private set; }
    public string? ImageKey { get; private set; }
    public string? DetailImageKey { get; private set; }
    public string Description { get; private set; }
    public string? ActivationGuideUrl { get; private set; }
    public string? ActivationType { get; private set; }

    public Product(
        string slug,
        string name,
        string platform,
        bool isActive = true,
        string? imageKey = null,
        string? description = null,
        string? detailImageKey = null,
        string? activationGuideUrl = null,
        string? activationType = null)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new DomainException("Product slug must not be empty.");
        }

        if (slug != slug.ToLowerInvariant())
        {
            throw new DomainException("Product slug must be lowercase.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Product name must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(platform))
        {
            throw new DomainException("Product platform must not be empty.");
        }

        Slug = slug;
        Name = name;
        Platform = platform;
        IsActive = isActive;
        ImageKey = imageKey;
        DetailImageKey = detailImageKey;
        Description = description ?? string.Empty;
        ActivationGuideUrl = activationGuideUrl;
        ActivationType = activationType;
    }

    /// <summary>
    /// Updates the mutable catalog fields of an existing product in place.
    /// <see cref="Slug"/> is the seeder's upsert key and is never changed here.
    /// Shares the same non-empty invariants as the constructor.
    /// </summary>
    public void UpdateCatalogInfo(
        string name,
        string platform,
        string? description,
        string? imageKey,
        string? detailImageKey,
        bool isActive,
        string? activationGuideUrl,
        string? activationType)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Product name must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(platform))
        {
            throw new DomainException("Product platform must not be empty.");
        }

        Name = name;
        Platform = platform;
        Description = description ?? string.Empty;
        ImageKey = imageKey;
        DetailImageKey = detailImageKey;
        IsActive = isActive;
        ActivationGuideUrl = activationGuideUrl;
        ActivationType = activationType;
    }

    /// <summary>
    /// Admin-only slug rename — kept separate from <see cref="UpdateCatalogInfo"/> so
    /// <c>CatalogSeeder</c>'s upsert-by-slug call path stays untouched by construction, not by
    /// convention. Same lowercase/non-empty invariant as the constructor; uniqueness against
    /// other products is the caller's responsibility (see <c>UpdateProduct</c>).
    /// </summary>
    public void RenameSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new DomainException("Product slug must not be empty.");
        }

        if (slug != slug.ToLowerInvariant())
        {
            throw new DomainException("Product slug must be lowercase.");
        }

        Slug = slug;
    }
}
