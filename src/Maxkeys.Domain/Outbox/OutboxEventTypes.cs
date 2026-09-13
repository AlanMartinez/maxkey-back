namespace Maxkeys.Domain.Outbox;

/// <summary>
/// Well-known <see cref="OutboxEvent.Type"/> values (design section 3;
/// outbox-processing spec: OrderApproved Handler).
/// </summary>
public static class OutboxEventTypes
{
    public const string OrderApproved = "OrderApproved";
    public const string OrderDelivered = "OrderDelivered";
}
