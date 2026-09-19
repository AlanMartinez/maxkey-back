using Maxkeys.Domain.Catalog;

namespace Maxkeys.Application.Catalog;

/// <summary>
/// Computes the display <c>OldPrice</c> for a variant from its persisted
/// <c>Price</c>/<c>DiscountPercentage</c> pair (design decision "OldPrice
/// computed in the read layer, not on the entity"; admin-catalog spec
/// "Variant Discount Percentage Pricing"). The single formula is shared by
/// every admin and public DTO mapper that surfaces <c>OldPrice</c>. Also owns
/// <see cref="PickDisplayVariant"/>, the shared rule for which active variant
/// drives the public <c>FromPrice</c>/<c>OldPrice</c>.
/// </summary>
public static class VariantPricing
{
    public static decimal? ComputeOldPrice(decimal price, decimal? discountPercentage) =>
        discountPercentage is null
            ? null
            : Math.Round(price / (1m - discountPercentage.Value / 100m), 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Picks the variant whose price the catalog card and product detail display:
    /// the one flagged <see cref="ProductVariant.IsRecommended"/> if present,
    /// otherwise the cheapest by <see cref="ProductVariant.Price"/> (stable order).
    /// Callers pass only active variants, so an inactive recommended variant
    /// naturally falls back to the cheapest active one. Returns
    /// <see langword="null"/> for an empty set.
    /// </summary>
    public static ProductVariant? PickDisplayVariant(IEnumerable<ProductVariant> activeVariants)
    {
        var variants = activeVariants as IReadOnlyCollection<ProductVariant> ?? activeVariants.ToList();
        return variants.FirstOrDefault(v => v.IsRecommended)
            ?? variants.OrderBy(v => v.Price).FirstOrDefault();
    }
}
