using Maxkeys.Domain.Common;
using Maxkeys.Domain.Keys;

namespace Maxkeys.Domain.Orders;

/// <summary>One line of an <see cref="Order"/>: snapshot names + unit price, quantity 1..10, plus the keys assigned to it.</summary>
public sealed class OrderItem : Entity
{
    private readonly List<Key> _keys = new();

    public Guid ProductVariantId { get; private set; }
    public string ProductNameSnapshot { get; private set; }
    public string VariantNameSnapshot { get; private set; }
    public decimal UnitPrice { get; private set; }
    public int Quantity { get; private set; }
    public IReadOnlyCollection<Key> Keys => _keys;

    /// <summary>Derived completion state (fulfillment spec: All-or-Nothing Delivery Derivation).</summary>
    public bool IsComplete => _keys.Count(k => k.Status == KeyStatus.Assigned) == Quantity;

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

    /// <summary>Called by <see cref="Order.AttachKey"/> once the key is assigned to this item.</summary>
    internal void AddKey(Key key) => _keys.Add(key);
}
