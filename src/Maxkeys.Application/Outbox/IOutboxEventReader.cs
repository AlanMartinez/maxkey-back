using Maxkeys.Domain.Outbox;

namespace Maxkeys.Application.Outbox;

/// <summary>
/// Read-side port for outbox rows keyed by the order they reference
/// (admin-buyers spec: Order Detail; ADR-01 — the <c>jsonb</c> containment
/// query needs the Npgsql provider, which Application must not reference, so
/// <c>Maxkeys.Infrastructure.Persistence.OutboxEventReader</c> implements it).
/// </summary>
public interface IOutboxEventReader
{
    /// <summary>
    /// Returns every <see cref="OutboxEvent"/> whose JSON payload's
    /// <c>orderId</c> equals <paramref name="orderId"/>, oldest first
    /// (<see cref="OutboxEvent.CreatedAt"/> ascending).
    /// </summary>
    Task<IReadOnlyList<OutboxEvent>> ListByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default);
}
