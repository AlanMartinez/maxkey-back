using Maxkeys.Domain.Common;

namespace Maxkeys.Domain.Notifications;

public static class NotificationTypes
{
    public const string OrderDelivered = "OrderDelivered";
}

public sealed class Notification : Entity
{
    private Notification()
    {
        Type = null!;
    }

    public Notification(Guid userId, Guid orderId, DateTime createdAt)
    {
        UserId = userId;
        Type = NotificationTypes.OrderDelivered;
        OrderId = orderId;
        CreatedAt = createdAt.ToUniversalTime();
    }

    public Guid UserId { get; private set; }
    public string Type { get; private set; }
    public Guid OrderId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? ReadAt { get; private set; }

    public void MarkRead(DateTime readAt)
    {
        if (ReadAt is null)
        {
            ReadAt = readAt.ToUniversalTime();
        }
    }
}
