namespace Maxkeys.Application.Catalog;

/// <summary>
/// Computes the display <c>OldPrice</c> for a variant from its persisted
/// <c>Price</c>/<c>DiscountPercentage</c> pair (design decision "OldPrice
/// computed in the read layer, not on the entity"; admin-catalog spec
/// "Variant Discount Percentage Pricing"). The single formula is shared by
/// every admin and public DTO mapper that surfaces <c>OldPrice</c>.
/// </summary>
public static class VariantPricing
{
    public static decimal? ComputeOldPrice(decimal price, decimal? discountPercentage) =>
        discountPercentage is null
            ? null
            : Math.Round(price / (1m - discountPercentage.Value / 100m), 2, MidpointRounding.AwayFromZero);
}
