using Maxkeys.Domain.Orders;

namespace Maxkeys.Application.Orders;

/// <summary>One row of the caller's order history (orders-history spec: My Orders Listing).</summary>
public sealed record OrderSummary(Guid Id, OrderStatus Status, decimal TotalAmount, string Currency, DateTimeOffset CreatedAt, int ItemCount);

/// <summary>
/// One order item's detail (admin-key-delivery-gate spec: decision 2/4 — MODIFIED).
/// <see cref="Keys"/> only ever contains codes already revealed by the buyer via
/// <c>RevealOrderItemKeys</c> — never auto-populated just because the order is
/// <see cref="OrderStatus.Delivered"/>. <see cref="Revealable"/> tells the caller
/// whether the "revelar key" action is available for this item right now.
/// </summary>
public sealed record MyOrderItemDetail(string ProductName, string VariantName, decimal UnitPrice, int Quantity, IReadOnlyList<string> Keys, bool Revealable);

/// <summary>Full order detail for the owning buyer, scoped by <see cref="GetMyOrder"/>.</summary>
public sealed record MyOrderDetail(
    Guid Id,
    OrderStatus Status,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset CreatedAt,
    IReadOnlyList<MyOrderItemDetail> Items);
