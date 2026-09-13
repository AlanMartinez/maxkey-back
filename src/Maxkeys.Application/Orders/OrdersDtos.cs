using Maxkeys.Domain.Orders;

namespace Maxkeys.Application.Orders;

/// <summary>One row of the caller's order history (orders-history spec: My Orders Listing).</summary>
public sealed record OrderSummary(Guid Id, OrderStatus Status, decimal TotalAmount, string Currency, DateTimeOffset CreatedAt, int ItemCount);

/// <summary>
/// One order item's detail. <see cref="Keys"/> is <see langword="null"/> unless the order is
/// <see cref="OrderStatus.Delivered"/> (orders-history spec: Order Detail With Conditional Key Reveal).
/// </summary>
public sealed record MyOrderItemDetail(string ProductName, string VariantName, decimal UnitPrice, int Quantity, IReadOnlyList<string>? Keys);

/// <summary>Full order detail for the owning buyer, scoped by <see cref="GetMyOrder"/>.</summary>
public sealed record MyOrderDetail(
    Guid Id,
    OrderStatus Status,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset CreatedAt,
    IReadOnlyList<MyOrderItemDetail> Items);
