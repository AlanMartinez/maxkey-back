using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Outbox;
using Maxkeys.Infrastructure.Outbox;
using Maxkeys.Infrastructure.Persistence;

namespace Maxkeys.Application.Tests.Outbox;

/// <summary>
/// Covers <see cref="OutboxClaimQuery"/> (outbox-processing spec: Batch Claim
/// With Row Locking, Multi-Instance Claim Safety): concurrent claimers never
/// double-claim and together cover all due rows; expired leases are
/// reclaimable, future leases and <c>Failed</c> rows are not.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OutboxClaimQueryTests
{
    private readonly PostgresFixture _fixture;

    public OutboxClaimQueryTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Two_concurrent_claimers_never_claim_the_same_row_and_together_cover_all_due_rows()
    {
        var now = DateTimeOffset.UtcNow;
        var eventIds = await SeedPendingEventsAsync(count: 6, now);
        await using var contextA = _fixture.CreateContext();
        await using var contextB = _fixture.CreateContext();

        var results = await Task.WhenAll(
            new OutboxClaimQuery().ClaimBatchAsync(contextA, now, batchSize: 3, TimeSpan.FromSeconds(60), CancellationToken.None),
            new OutboxClaimQuery().ClaimBatchAsync(contextB, now, batchSize: 3, TimeSpan.FromSeconds(60), CancellationToken.None));
        var claimedIds = results[0].Select(e => e.Id).Concat(results[1].Select(e => e.Id)).ToList();

        Assert.Equal(claimedIds.Count, claimedIds.Distinct().Count());
        Assert.Equal(eventIds.Count, claimedIds.Count);
        Assert.All(eventIds, id => Assert.Contains(id, claimedIds));
    }

    [Fact]
    public async Task Claim_reclaims_expired_leases_and_excludes_future_leases_and_failed_rows()
    {
        var now = DateTimeOffset.UtcNow;
        var expiredLeaseId = await SeedEventAsync(now, e => e.Claim(now.AddSeconds(-10)));
        var futureLeaseId = await SeedEventAsync(now, e => e.Claim(now.AddSeconds(120)));
        var failedId = await SeedEventAsync(now, e => e.MarkFailedAttempt("boom", now, maxAttempts: 1));

        var claimedIds = (await ClaimAsync(now)).Select(e => e.Id).ToList();

        Assert.Contains(expiredLeaseId, claimedIds);
        Assert.DoesNotContain(futureLeaseId, claimedIds);
        Assert.DoesNotContain(failedId, claimedIds);
    }

    private async Task<IReadOnlyList<OutboxEvent>> ClaimAsync(DateTimeOffset now)
    {
        await using var context = _fixture.CreateContext();
        return await new OutboxClaimQuery()
            .ClaimBatchAsync(context, now, batchSize: 10, TimeSpan.FromSeconds(60), CancellationToken.None);
    }

    private async Task<List<Guid>> SeedPendingEventsAsync(int count, DateTimeOffset now)
    {
        await using var context = _fixture.CreateContext();
        var events = Enumerable.Range(0, count).Select(_ => new OutboxEvent(OutboxEventTypes.OrderApproved, "{}", now)).ToList();
        context.OutboxEvents.AddRange(events);
        await context.SaveChangesAsync();
        return events.Select(e => e.Id).ToList();
    }

    /// <summary>Seeds one <c>Pending</c> event then applies <paramref name="configure"/> (e.g. <c>Claim</c>/<c>MarkFailedAttempt</c>) to reach the state under test.</summary>
    private async Task<Guid> SeedEventAsync(DateTimeOffset now, Action<OutboxEvent> configure)
    {
        await using var context = _fixture.CreateContext();
        var evt = new OutboxEvent(OutboxEventTypes.OrderApproved, "{}", now);
        configure(evt);
        context.OutboxEvents.Add(evt);
        await context.SaveChangesAsync();
        return evt.Id;
    }
}
