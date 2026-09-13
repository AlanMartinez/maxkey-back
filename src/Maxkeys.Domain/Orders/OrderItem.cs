using Maxkeys.Domain.Common;

namespace Maxkeys.Domain.Orders;

/// <summary>One line of an <see cref="Order"/>: snapshot names + unit price, quantity 1..10. Keys/`IsComplete` added in PR2b.</summary>
public sealed class OrderItem : Entity
{
    public Guid ProductVariantId { get; private set; }
    public string ProductNameSnapshot { get; private set; }
    public string VariantNameSnapshot { get; private set; }
    public decimal UnitPrice { get; private set; }
    public int Quantity { get; private set; }

    public OrderItem(Guid productVariantId, string productNameSnapshot, string variantNameSnapshot, decimal unitPrice, int quantity)
    {
        if (string.IsNullOrWhiteSpace(productNameSnapshot))
        {
            throw new DomainException("Order item product name snapshot must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(variantNameSnapshot))
        {
            throw new DomainException("Order item variant name snapshot must not be empty.");
        }

        if (quantity < 1 || quantity > 10)
        {
            throw new DomainException("Order item quantity must be between 1 and 10.");
        }

        ProductVariantId = productVariantId;
        ProductNameSnapshot = productNameSnapshot;
        VariantNameSnapshot = variantNameSnapshot;
        UnitPrice = unitPrice;
        Quantity = quantity;
    }
}
