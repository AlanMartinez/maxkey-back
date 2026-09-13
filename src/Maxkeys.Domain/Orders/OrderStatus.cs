namespace Maxkeys.Domain.Orders;

/// <summary>Order lifecycle status (design section 4.2). `Delivered` is derived by `Order.AttachKey`.</summary>
public enum OrderStatus
{
    Pending,
    Paid,
    AwaitingFulfillment,
    Delivered,
    Cancelled
}
