using Maxkeys.Domain.Common;

namespace Maxkeys.Domain.Catalog;

/// <summary>
/// A sellable product (e.g. a gift card family). Variants (region/edition/tier)
/// are added by the catalog seeder, not by domain methods — the catalog is
/// seed-managed in the MVP (design section 4.1). <see cref="UpdateCatalogInfo"/>
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

    public Product(
        string slug,
        string name,
        string platform,
        bool isActive = true,
        string? imageKey = null,
        string? description = null,
        string? detailImageKey = null)
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
    }

    /// <summary>
    /// Updates the mutable catalog fields of an existing product in place.
    /// <see cref="Slug"/> is the seeder's upsert key and is never changed here.
    /// Shares the same non-empty invariants as the constructor.
    /// </summary>
    public void UpdateCatalogInfo(
        string name, string platform, string? description, string? imageKey, string? detailImageKey, bool isActive)
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
    }
}
