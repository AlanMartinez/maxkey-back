namespace Maxkeys.Domain.Orders;

/// <summary>Order lifecycle status (design section 4.2). `Delivered` is derived via `AttachKey` in PR2b, not implemented here.</summary>
public enum OrderStatus
{
    Pending,
    Paid,
    AwaitingFulfillment,
    Delivered,
    Cancelled
}
