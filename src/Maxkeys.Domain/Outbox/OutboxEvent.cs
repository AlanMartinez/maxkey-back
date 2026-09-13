using Maxkeys.Domain.Common;

namespace Maxkeys.Domain.Outbox;

/// <summary>
/// A transactional outbox row (design section 4.1, section 6). Inserted in the
/// same transaction as the domain change it announces, then claimed and
/// dispatched by the out-of-process <c>OutboxProcessor</c> (PR7).
/// </summary>
public sealed class OutboxEvent : Entity
{
    public string Type { get; private set; }
    public string Payload { get; private set; }
    public OutboxEventStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }

    /// <summary>EF Core materialization constructor (ADR-01) — properties are set by the ORM via their private setters.</summary>
    private OutboxEvent()
    {
        Type = null!;
        Payload = null!;
    }

    public OutboxEvent(string type, string payload, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            throw new DomainException("Outbox event type must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new DomainException("Outbox event payload must not be empty.");
        }

        Type = type;
        Payload = payload;
        Status = OutboxEventStatus.Pending;
        Attempts = 0;
        NextAttemptAt = now;
        CreatedAt = now;
    }

    /// <summary>
    /// Marks the row <see cref="OutboxEventStatus.Processing"/> with a lease
    /// (outbox-processing spec: Batch Claim With Row Locking — the
    /// <c>SELECT ... FOR UPDATE SKIP LOCKED</c> query calls this in the same
    /// transaction as the row lock). Not callable once the event reached a
    /// terminal state.
    /// </summary>
    public void Claim(DateTimeOffset leaseUntil)
    {
        if (Status is OutboxEventStatus.Processed or OutboxEventStatus.Failed)
        {
            throw new DomainConflictException($"Cannot claim an outbox event in status {Status}.");
        }

        Status = OutboxEventStatus.Processing;
        NextAttemptAt = leaseUntil;
    }

    public void MarkProcessed(DateTimeOffset now)
    {
        Status = OutboxEventStatus.Processed;
        ProcessedAt = now;
    }

    /// <summary>
    /// Records a failed handling attempt (outbox-processing spec: Exponential
    /// Backoff on Failure, Dead-Letter After Max Attempts). Increments
    /// <see cref="Attempts"/> first, then:
    /// <list type="bullet">
    /// <item>if the new <see cref="Attempts"/> has reached
    /// <paramref name="maxAttempts"/> (8 per design section 6c), the event is
    /// dead-lettered as <see cref="OutboxEventStatus.Failed"/> and excluded
    /// from future claim batches;</item>
    /// <item>otherwise the event returns to <see cref="OutboxEventStatus.Pending"/>
    /// with <c>NextAttemptAt = now + 30s * 2^Attempts</c> (attempt 1 -&gt; ~60s,
    /// attempt 2 -&gt; ~120s, attempt 3 -&gt; ~240s, ...).</item>
    /// </list>
    /// </summary>
    public void MarkFailedAttempt(string error, DateTimeOffset now, int maxAttempts)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            throw new DomainException("Outbox failure error must not be empty.");
        }

        Attempts++;
        LastError = error;

        if (Attempts >= maxAttempts)
        {
            Status = OutboxEventStatus.Failed;
            return;
        }

        Status = OutboxEventStatus.Pending;
        NextAttemptAt = now + TimeSpan.FromSeconds(30 * Math.Pow(2, Attempts));
    }
}
