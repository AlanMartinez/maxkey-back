using Maxkeys.Application.Outbox;
using Maxkeys.Application.Tests.Fakes;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Outbox;
using Maxkeys.Infrastructure.Outbox;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Tests.Outbox;

/// <summary>
/// Covers <see cref="OutboxProcessor.ProcessOnceAsync"/> (outbox-processing
/// spec: Exponential Backoff on Failure, Dead-Letter After Max Attempts),
/// driving a single pass directly instead of the poll loop.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OutboxProcessorTests
{
    private readonly PostgresFixture _fixture;

    public OutboxProcessorTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Failing_handler_reschedules_with_backoff_then_dead_letters_after_max_attempts()
    {
        const int maxAttempts = 3;
        var eventId = await SeedPendingEventAsync();
        var handler = new FakeOutboxHandler { EventType = OutboxEventTypes.OrderApproved, ExceptionToThrow = new InvalidOperationException("boom") };
        var before = DateTimeOffset.UtcNow;

        await RunOnceAsync(handler, maxAttempts);

        var evt = await LoadEventAsync(eventId);
        Assert.Equal(OutboxEventStatus.Pending, evt.Status);
        Assert.Equal(1, evt.Attempts);
        Assert.InRange(evt.NextAttemptAt, before.AddSeconds(55), before.AddSeconds(70));

        // Backoff pushed NextAttemptAt into the future; simulate the remaining prior failure directly (far in the
        // past, still due) instead of waiting for real time, then let one more real pass dead-letter it.
        await using var context = _fixture.CreateContext();
        var tracked = await context.OutboxEvents.SingleAsync(e => e.Id == eventId);
        tracked.MarkFailedAttempt("prior failure", DateTimeOffset.UtcNow.AddDays(-1), maxAttempts: maxAttempts + 1000);
        await context.SaveChangesAsync();

        await RunOnceAsync(handler, maxAttempts);

        evt = await LoadEventAsync(eventId);
        Assert.Equal(OutboxEventStatus.Failed, evt.Status);
        Assert.Equal(maxAttempts, evt.Attempts);
    }

    [Fact]
    public async Task Succeeding_handler_marks_the_event_processed()
    {
        var eventId = await SeedPendingEventAsync();
        var handler = new FakeOutboxHandler { EventType = OutboxEventTypes.OrderApproved };

        await RunOnceAsync(handler, maxAttempts: 8);

        var evt = await LoadEventAsync(eventId);
        Assert.Equal(OutboxEventStatus.Processed, evt.Status);
        Assert.NotNull(evt.ProcessedAt);
        Assert.Single(handler.HandledEvents);
    }

    private async Task RunOnceAsync(FakeOutboxHandler handler, int maxAttempts)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(_fixture.ConnectionString));
        services.AddScoped<IOutboxHandler>(_ => handler);
        await using var provider = services.BuildServiceProvider();

        var options = Options.Create(new OutboxOptions { BatchSize = 20, LeaseSeconds = 60, MaxAttempts = maxAttempts });
        var processor = new OutboxProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(), new OutboxClaimQuery(), options, NullLogger<OutboxProcessor>.Instance);
        await processor.ProcessOnceAsync(CancellationToken.None);
    }

    private async Task<OutboxEvent> LoadEventAsync(Guid eventId)
    {
        await using var context = _fixture.CreateContext();
        return await context.OutboxEvents.SingleAsync(e => e.Id == eventId);
    }

    private async Task<Guid> SeedPendingEventAsync()
    {
        await using var context = _fixture.CreateContext();
        var evt = new OutboxEvent(OutboxEventTypes.OrderApproved, "{}", DateTimeOffset.UtcNow);
        context.OutboxEvents.Add(evt);
        await context.SaveChangesAsync();
        return evt.Id;
    }
}
