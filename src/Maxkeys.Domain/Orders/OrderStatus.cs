namespace Maxkeys.Domain.Orders;

/// <summary>Order lifecycle status (design section 4.2). `KeysAssigned` and `Delivered` are derived/set by `Order.AttachKey`/`Order.MarkDelivered`.</summary>
public enum OrderStatus
{
    Pending,
    Paid,
    AwaitingFulfillment,
    Delivered,
    Cancelled,
    KeysAssigned
}
