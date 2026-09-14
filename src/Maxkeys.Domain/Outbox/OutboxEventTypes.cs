namespace Maxkeys.Domain.Outbox;

/// <summary>
/// Well-known <see cref="OutboxEvent.Type"/> values (design section 3;
/// outbox-processing spec: OrderApproved Handler).
/// </summary>
public static class OutboxEventTypes
{
    public const string OrderApproved = "OrderApproved";
    public const string OrderDelivered = "OrderDelivered";

    /// <summary>Admin-triggered resend of the delivery email for an already-<c>Delivered</c> order (admin-buyers spec: Resend Delivery Email; design D1).</summary>
    public const string OrderDeliveryResendRequested = "OrderDeliveryResendRequested";
}
