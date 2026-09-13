using Maxkeys.Domain.Common;

namespace Maxkeys.Domain.Catalog;

/// <summary>
/// A purchasable variant of a <see cref="Product"/> (region/edition/tier),
/// each with its own ARS price. No transition methods in the MVP — variants
/// are seed-managed (design section 4.1).
/// </summary>
public sealed class ProductVariant : Entity
{
    public Guid ProductId { get; private set; }
    public decimal Price { get; private set; }
    public decimal? OldPrice { get; private set; }
    public string Currency { get; private set; }
    public string? Region { get; private set; }
    public string? Edition { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }

    public ProductVariant(
        Guid productId,
        decimal price,
        string currency,
        decimal? oldPrice = null,
        string? region = null,
        string? edition = null,
        int sortOrder = 0,
        bool isActive = true)
    {
        if (price <= 0)
        {
            throw new DomainException("Variant price must be greater than zero.");
        }

        if (oldPrice is not null && oldPrice <= price)
        {
            throw new DomainException("Variant old price must be greater than the current price.");
        }

        if (currency != "ARS")
        {
            throw new DomainException("Variant currency must be ARS.");
        }

        ProductId = productId;
        Price = price;
        OldPrice = oldPrice;
        Currency = currency;
        Region = region;
        Edition = edition;
        SortOrder = sortOrder;
        IsActive = isActive;
    }
}
