namespace Maxkeys.Application.Buyers;

/// <summary>One key attached to an order item in the admin order detail — status, timestamps, and who revealed it, never the code (admin-buyers spec: Key Exposure in Buyer View; admin-key-delivery-gate spec: decision 2).</summary>
public sealed record AdminOrderDetailKey(
    Guid KeyId, string Status, DateTimeOffset? AssignedAt, DateTimeOffset? RevealedAt, string? RevealedBy);

/// <summary>One order item in the admin order detail, with its price/name snapshots and attached keys (admin-buyers spec: Order Detail).</summary>
public sealed record AdminOrderDetailItem(
    Guid ItemId,
    string ProductName,
    string VariantName,
    decimal UnitPrice,
    int Quantity,
    IReadOnlyList<AdminOrderDetailKey> Keys);

/// <summary>One outbox row referencing the order, for the admin order detail timeline (admin-buyers spec: Order Detail; design section 6).</summary>
public sealed record AdminOrderDetailEvent(
    Guid Id,
    string Type,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ProcessedAt,
    int Attempts,
    string? LastError);

/// <summary>Full order detail for the admin buyers modal (admin-buyers spec: Order Detail). Result of <see cref="GetAdminOrderDetail"/>.</summary>
public sealed record AdminOrderDetail(
    Guid Id,
    string BuyerEmail,
    string Status,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset UpdatedAt,
    string? MpPaymentId,
    string? LastPaymentAttemptStatus,
    DateTimeOffset? LastPaymentAttemptAt,
    IReadOnlyList<AdminOrderDetailItem> Items,
    IReadOnlyList<AdminOrderDetailEvent> Events);
