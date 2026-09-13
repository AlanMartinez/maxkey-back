using Maxkeys.Domain.Orders;

namespace Maxkeys.Application.Fulfillment;

/// <summary>Per-item progress after an admin attaches a key (fulfillment spec: Key Attachment).</summary>
public sealed record AttachKeyToOrderItemResultItem(Guid ItemId, int Quantity, int Assigned);

/// <summary>Order status and per-item progress returned by <see cref="AttachKeyToOrderItem"/>.</summary>
public sealed record AttachKeyToOrderItemResult(OrderStatus OrderStatus, IReadOnlyList<AttachKeyToOrderItemResultItem> Items);

/// <summary>Per-item fulfillment progress row for the admin listing (fulfillment spec: Admin Order Listing).</summary>
public sealed record AdminOrderItemSummary(Guid ItemId, Guid VariantId, string ProductName, string VariantName, int Quantity, int Assigned);

/// <summary>One order awaiting fulfillment, with per-item progress, for the admin listing.</summary>
public sealed record AdminOrderSummary(
    Guid Id,
    string BuyerEmail,
    OrderStatus Status,
    DateTimeOffset? PaidAt,
    IReadOnlyList<AdminOrderItemSummary> Items);
