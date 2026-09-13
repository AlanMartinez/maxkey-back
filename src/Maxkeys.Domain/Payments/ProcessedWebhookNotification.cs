using Maxkeys.Domain.Common;

namespace Maxkeys.Domain.Payments;

/// <summary>
/// Dedupe record for a processed Mercado Pago webhook notification
/// (payments-webhook spec: Notification Deduplication). Insert-only —
/// <see cref="RequestId"/> is the primary key so a repeated notification is
/// rejected by a PK violation rather than a read-then-write race
/// (design section 5).
/// </summary>
public sealed class ProcessedWebhookNotification
{
    public string RequestId { get; }
    public string PaymentId { get; }
    public Guid? OrderId { get; }
    public DateTimeOffset ReceivedAt { get; }

    public ProcessedWebhookNotification(string requestId, string paymentId, Guid? orderId, DateTimeOffset receivedAt)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            throw new DomainException("Webhook notification request id must not be empty.");
        }

        RequestId = requestId;
        PaymentId = paymentId;
        OrderId = orderId;
        ReceivedAt = receivedAt;
    }
}
