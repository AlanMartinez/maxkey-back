namespace Maxkeys.Domain.Outbox;

public enum OutboxEventStatus
{
    Pending,
    Processing,
    Processed,
    Failed,
}
