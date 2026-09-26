using Maxkeys.Domain.Common;
using Maxkeys.Domain.Outbox;

namespace Maxkeys.Domain.Tests.Outbox;

public class OutboxEventTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private const int MaxAttempts = 8;

    private static OutboxEvent CreatePendingEvent() =>
        new(OutboxEventTypes.OrderApproved, "{}", Now);

    [Fact]
    public void Claim_SetsProcessingAndLease()
    {
        var outboxEvent = CreatePendingEvent();
        var leaseUntil = Now.AddSeconds(30);

        outboxEvent.Claim(leaseUntil);

        Assert.Equal(OutboxEventStatus.Processing, outboxEvent.Status);
        Assert.Equal(leaseUntil, outboxEvent.NextAttemptAt);
    }

    [Fact]
    public void Claim_WhenAlreadyProcessed_Throws()
    {
        var outboxEvent = CreatePendingEvent();
        outboxEvent.Claim(Now.AddSeconds(30));
        outboxEvent.MarkProcessed(Now);

        Assert.Throws<DomainConflictException>(() => outboxEvent.Claim(Now.AddSeconds(60)));
    }

    [Fact]
    public void MarkProcessed_SetsProcessedStatusAndTimestamp()
    {
        var outboxEvent = CreatePendingEvent();
        outboxEvent.Claim(Now.AddSeconds(30));

        outboxEvent.MarkProcessed(Now.AddSeconds(1));

        Assert.Equal(OutboxEventStatus.Processed, outboxEvent.Status);
        Assert.Equal(Now.AddSeconds(1), outboxEvent.ProcessedAt);
    }

    [Fact]
    public void MarkProcessed_AfterFailedAttempt_ClearsLastError()
    {
        var outboxEvent = CreatePendingEvent();
        outboxEvent.Claim(Now.AddSeconds(30));
        outboxEvent.MarkFailedAttempt("No address found.", Now, maxAttempts: 8);
        outboxEvent.Claim(Now.AddSeconds(120));

        outboxEvent.MarkProcessed(Now.AddSeconds(90));

        Assert.Null(outboxEvent.LastError);
    }

    [Theory]
    [InlineData(0, 1, 60)]   // 30s * 2^1 = 60s
    [InlineData(1, 2, 120)]  // 30s * 2^2 = 120s
    [InlineData(2, 3, 240)]  // 30s * 2^3 = 240s
    public void MarkFailedAttempt_AppliesExponentialBackoff(int attemptsBefore, int expectedAttempts, int expectedDelaySeconds)
    {
        var outboxEvent = CreatePendingEvent();
        for (var i = 0; i < attemptsBefore; i++)
        {
            outboxEvent.MarkFailedAttempt("boom", Now, MaxAttempts);
        }

        outboxEvent.MarkFailedAttempt("boom", Now, MaxAttempts);

        Assert.Equal(expectedAttempts, outboxEvent.Attempts);
        Assert.Equal(OutboxEventStatus.Pending, outboxEvent.Status);
        Assert.Equal(Now.AddSeconds(expectedDelaySeconds), outboxEvent.NextAttemptAt);
    }

    [Fact]
    public void MarkFailedAttempt_OnEighthFailure_DeadLettersEvent()
    {
        var outboxEvent = CreatePendingEvent();
        for (var i = 0; i < MaxAttempts - 1; i++)
        {
            outboxEvent.MarkFailedAttempt("boom", Now, MaxAttempts);
        }

        Assert.Equal(OutboxEventStatus.Pending, outboxEvent.Status);

        outboxEvent.MarkFailedAttempt("boom", Now, MaxAttempts);

        Assert.Equal(MaxAttempts, outboxEvent.Attempts);
        Assert.Equal(OutboxEventStatus.Failed, outboxEvent.Status);
    }

    [Fact]
    public void Constructor_WithEmptyType_Throws()
    {
        Assert.Throws<DomainException>(() => new OutboxEvent("", "{}", Now));
    }
}
