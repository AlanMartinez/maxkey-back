using Maxkeys.Domain.Outbox;

namespace Maxkeys.Application.Outbox;

/// <summary>
/// Business logic for one <see cref="OutboxEvent.Type"/> value, invoked by the
/// out-of-process <c>OutboxProcessor</c> (PR7b; design section 3, section 6c;
/// ADR-03 — a fourth interface accepted because two real implementations exist:
/// <see cref="OrderApprovedHandler"/> and <see cref="OrderDeliveryResendHandler"/>).
/// The processor stays ignorant of business logic: it only claims rows and
/// dispatches by <see cref="EventType"/>.
/// </summary>
public interface IOutboxHandler
{
    /// <summary>The <see cref="OutboxEvent.Type"/> this handler processes.</summary>
    string EventType { get; }

    /// <summary>
    /// Processes <paramref name="evt"/>. Implementations MUST be idempotent —
    /// the processor may invoke this more than once for the same row (retry,
    /// at-least-once) — and MUST throw to signal failure so the processor can
    /// reschedule with backoff or dead-letter (outbox-processing spec:
    /// Exponential Backoff on Failure, Dead-Letter After Max Attempts).
    /// </summary>
    Task HandleAsync(OutboxEvent evt, CancellationToken cancellationToken);
}
