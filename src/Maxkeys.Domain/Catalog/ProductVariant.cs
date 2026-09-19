using Maxkeys.Domain.Common;

namespace Maxkeys.Domain.Catalog;

/// <summary>
/// A purchasable variant of a <see cref="Product"/> (region/edition/tier),
/// each with its own ARS/USD price. Variants are seed-managed (design section
/// 4.1); <see cref="UpdateDetails"/> is the one mutation method, added in PR12
/// so <c>CatalogSeeder</c> (ADR-12) can upsert an existing row instead of only
/// inserting. <see cref="DiscountPercentage"/> is the sole discount input
/// (admin-catalog spec "Variant Discount Percentage Pricing"); the display
/// <c>OldPrice</c> is a read-time computation (<c>VariantPricing.ComputeOldPrice</c>),
/// not persisted state. <see cref="IsRecommended"/> marks the variant whose price
/// the catalog card shows and that the detail page preselects; at most one
/// variant per product may carry it, enforced by the <c>UpdateProductVariant</c>
/// use case (clears the sibling flag) and a DB partial unique index on
/// <c>product_id WHERE is_recommended</c>.
/// </summary>
public sealed class ProductVariant : Entity
{
    private static readonly string[] SupportedCurrencies = ["ARS", "USD"];

    public Guid ProductId { get; private set; }
    public decimal Price { get; private set; }
    public decimal? DiscountPercentage { get; private set; }
    public string Currency { get; private set; }
    public string? Region { get; private set; }
    public string? Edition { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsRecommended { get; private set; }

    public ProductVariant(
        Guid productId,
        decimal price,
        string currency,
        decimal? discountPercentage = null,
        string? region = null,
        string? edition = null,
        int sortOrder = 0,
        bool isActive = true)
    {
        Validate(price, discountPercentage, currency);

        ProductId = productId;
        Price = price;
        DiscountPercentage = discountPercentage;
        Currency = currency;
        Region = region;
        Edition = edition;
        SortOrder = sortOrder;
        IsActive = isActive;
        IsRecommended = false;
    }

    /// <summary>Updates all mutable fields in place. Shares the same invariants as the constructor.</summary>
    public void UpdateDetails(
        decimal price,
        decimal? discountPercentage,
        string currency,
        string? region,
        string? edition,
        int sortOrder,
        bool isActive)
    {
        Validate(price, discountPercentage, currency);

        Price = price;
        DiscountPercentage = discountPercentage;
        Currency = currency;
        Region = region;
        Edition = edition;
        SortOrder = sortOrder;
        IsActive = isActive;
    }

    /// <summary>
    /// Sets the recommended flag. Kept separate from <see cref="UpdateDetails"/> so
    /// the seeder's signature stays untouched; per-product exclusivity is the
    /// caller's responsibility (see class summary).
    /// </summary>
    public void SetRecommended(bool recommended)
    {
        IsRecommended = recommended;
    }

    /// <summary>Shared invariants for the constructor and <see cref="UpdateDetails"/> (design D2).</summary>
    private static void Validate(decimal price, decimal? discountPercentage, string currency)
    {
        if (price <= 0)
        {
            throw new DomainException("Variant price must be greater than zero.");
        }

        if (discountPercentage is not null && (discountPercentage <= 0 || discountPercentage >= 100))
        {
            throw new DomainException("Variant discount percentage must be between 0 and 100, exclusive.");
        }

        if (!SupportedCurrencies.Contains(currency))
        {
            throw new DomainException("Variant currency must be ARS or USD.");
        }
    }
}
