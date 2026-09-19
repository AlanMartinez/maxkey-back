using Maxkeys.Application.Outbox;
using Maxkeys.Domain.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Infrastructure.Persistence;

/// <summary>
/// Npgsql-backed <see cref="IOutboxEventReader"/>: matches rows with a
/// <c>jsonb</c> containment check (<c>payload @&gt; '{"orderId":"..."}'</c>),
/// which is exact on the JSON value rather than a substring scan of the
/// serialized text (admin-buyers spec: Order Detail).
/// </summary>
public sealed class OutboxEventReader : IOutboxEventReader
{
    private readonly AppDbContext _db;

    public OutboxEventReader(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<OutboxEvent>> ListByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var pattern = $$"""{"orderId":"{{orderId}}"}""";

        return await _db.OutboxEvents
            .AsNoTracking()
            .Where(e => EF.Functions.JsonContains(e.Payload, pattern))
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);
    }
}
