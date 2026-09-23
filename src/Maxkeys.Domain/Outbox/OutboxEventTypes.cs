namespace Maxkeys.Domain.Outbox;

/// <summary>
/// Well-known <see cref="OutboxEvent.Type"/> values (design section 3;
/// outbox-processing spec: OrderApproved Handler).
/// </summary>
public static class OutboxEventTypes
{
    public const string OrderApproved = "OrderApproved";

    /// <summary>Admin-triggered resend of the delivery email for an already-<c>Delivered</c> order (admin-buyers spec: Resend Delivery Email; design D1).</summary>
    public const string OrderDeliveryResendRequested = "OrderDeliveryResendRequested";

    /// <summary>Raised by <c>DeliverOrder</c> when an order transitions to <see cref="Maxkeys.Domain.Orders.OrderStatus.Delivered"/> (admin-key-delivery-gate spec: decision 4 revisited) — <c>OrderDeliveredHandler</c> notifies the buyer their keys are available, without sending key codes.</summary>
    public const string OrderDelivered = "OrderDelivered";
}
