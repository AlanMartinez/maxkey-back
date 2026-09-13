using Maxkeys.Domain.Outbox;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Infrastructure.Outbox;

/// <summary>
/// Claims due <see cref="OutboxEvent"/> rows via <c>SELECT ... FOR UPDATE SKIP LOCKED</c>,
/// marking them <see cref="OutboxEventStatus.Processing"/> with a fresh lease in the same
/// transaction (outbox-processing spec: Batch Claim With Row Locking, Multi-Instance Claim
/// Safety; ADR-06). Status is stored as text — literals below must match the enum names.
/// </summary>
public sealed class OutboxClaimQuery
{
    public async Task<IReadOnlyList<OutboxEvent>> ClaimBatchAsync(
        AppDbContext db, DateTimeOffset now, int batchSize, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var due = await db.Set<OutboxEvent>()
            .FromSqlInterpolated($@"
                SELECT * FROM outbox_events
                WHERE (status = 'Pending' AND next_attempt_at <= {now})
                   OR (status = 'Processing' AND next_attempt_at <= {now})
                ORDER BY next_attempt_at
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED")
            .ToListAsync(cancellationToken);

        var leaseUntil = now + leaseDuration;
        foreach (var evt in due) evt.Claim(leaseUntil);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return due;
    }
}
