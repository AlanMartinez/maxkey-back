using Maxkeys.Application.Payments;
using Maxkeys.Application.Tests.Fakes;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Orders;
using Maxkeys.Domain.Outbox;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maxkeys.Application.Tests.Payments;

[Collection(PostgresCollection.Name)]
public sealed class ProcessPaymentNotificationTests
{
    private readonly PostgresFixture _fixture;

    public ProcessPaymentNotificationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Same_request_id_repeated_processes_exactly_once()
    {
        var (orderId, amount) = await SeedPendingOrderAsync();
        var paymentId = UniquePaymentId();
        var gateway = new FakePaymentGateway { PaymentToReturn = new(paymentId, "approved", orderId.ToString(), amount, "ARS") };
        var requestId = $"req-{Guid.NewGuid():N}";

        await using var context = _fixture.CreateContext();
        var sut = new ProcessPaymentNotification(context, gateway, NullLogger<ProcessPaymentNotification>.Instance);

        var first = await sut.ExecuteAsync(requestId, paymentId);
        var second = await sut.ExecuteAsync(requestId, paymentId);
        var third = await sut.ExecuteAsync(requestId, paymentId);

        Assert.Equal(ProcessPaymentNotificationOutcome.Approved, first.Outcome);
        Assert.Equal(ProcessPaymentNotificationOutcome.Duplicate, second.Outcome);
        Assert.Equal(ProcessPaymentNotificationOutcome.Duplicate, third.Outcome);
        Assert.Single(gateway.GetPaymentCalls);

        await using var readContext = _fixture.CreateContext();
        Assert.Equal(OrderStatus.Paid, (await readContext.Orders.SingleAsync(o => o.Id == orderId)).Status);
        Assert.Equal(1, await CountOrderApprovedEventsAsync(readContext, orderId));
    }

    [Fact]
    public async Task Different_request_id_after_paid_is_a_noop()
    {
        var (orderId, amount) = await SeedPendingOrderAsync();
        var paymentId = UniquePaymentId();
        var gateway = new FakePaymentGateway { PaymentToReturn = new(paymentId, "approved", orderId.ToString(), amount, "ARS") };

        await using var context = _fixture.CreateContext();
        var sut = new ProcessPaymentNotification(context, gateway, NullLogger<ProcessPaymentNotification>.Instance);

        var first = await sut.ExecuteAsync("req-a", paymentId);
        var second = await sut.ExecuteAsync("req-b", paymentId);

        Assert.Equal(ProcessPaymentNotificationOutcome.Approved, first.Outcome);
        Assert.Equal(ProcessPaymentNotificationOutcome.Ignored, second.Outcome);
        Assert.Equal("state_guard", second.Reason);
        Assert.Equal(1, await CountOrderApprovedEventsAsync(context, orderId));
    }

    [Fact]
    public async Task Concurrent_duplicate_approval_yields_exactly_one_transition()
    {
        var (orderId, amount) = await SeedPendingOrderAsync();
        var paymentId = UniquePaymentId();
        var gatewayA = new FakePaymentGateway { PaymentToReturn = new(paymentId, "approved", orderId.ToString(), amount, "ARS") };
        var gatewayB = new FakePaymentGateway { PaymentToReturn = new(paymentId, "approved", orderId.ToString(), amount, "ARS") };

        await using var contextA = _fixture.CreateContext();
        await using var contextB = _fixture.CreateContext();
        var sutA = new ProcessPaymentNotification(contextA, gatewayA, NullLogger<ProcessPaymentNotification>.Instance);
        var sutB = new ProcessPaymentNotification(contextB, gatewayB, NullLogger<ProcessPaymentNotification>.Instance);

        var results = await Task.WhenAll(
            sutA.ExecuteAsync("req-conc-a", paymentId),
            sutB.ExecuteAsync("req-conc-b", paymentId));

        Assert.Single(results, r => r.Outcome == ProcessPaymentNotificationOutcome.Approved);
        Assert.All(
            results.Where(r => r.Outcome != ProcessPaymentNotificationOutcome.Approved),
            r => Assert.Equal(ProcessPaymentNotificationOutcome.Ignored, r.Outcome));

        await using var readContext = _fixture.CreateContext();
        Assert.Equal(OrderStatus.Paid, (await readContext.Orders.SingleAsync(o => o.Id == orderId)).Status);
        Assert.Equal(1, await CountOrderApprovedEventsAsync(readContext, orderId));
    }

    [Fact]
    public async Task Amount_mismatch_is_ignored_without_transition()
    {
        var (orderId, amount) = await SeedPendingOrderAsync();
        var paymentId = UniquePaymentId();
        var gateway = new FakePaymentGateway { PaymentToReturn = new(paymentId, "approved", orderId.ToString(), amount + 1m, "ARS") };

        await using var context = _fixture.CreateContext();
        var sut = new ProcessPaymentNotification(context, gateway, NullLogger<ProcessPaymentNotification>.Instance);

        var result = await sut.ExecuteAsync("req-mismatch", paymentId);

        Assert.Equal(ProcessPaymentNotificationOutcome.Ignored, result.Outcome);
        Assert.Equal("amount_mismatch", result.Reason);
        Assert.Equal(OrderStatus.Pending, (await context.Orders.SingleAsync(o => o.Id == orderId)).Status);
        Assert.Equal(0, await CountOrderApprovedEventsAsync(context, orderId));
    }

    [Fact]
    public async Task Rejected_is_recorded_then_later_approval_transitions_once()
    {
        var (orderId, amount) = await SeedPendingOrderAsync();
        var gateway = new FakePaymentGateway { PaymentToReturn = new("pay-rejected", "rejected", orderId.ToString(), amount, "ARS") };

        await using var context = _fixture.CreateContext();
        var sut = new ProcessPaymentNotification(context, gateway, NullLogger<ProcessPaymentNotification>.Instance);

        var rejected = await sut.ExecuteAsync("req-rejected", "pay-rejected");
        Assert.Equal(ProcessPaymentNotificationOutcome.AttemptRecorded, rejected.Outcome);

        var order = await context.Orders.SingleAsync(o => o.Id == orderId);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal("pay-rejected", order.LastPaymentAttemptId);
        Assert.Equal("rejected", order.LastPaymentAttemptStatus);
        Assert.NotNull(order.LastPaymentAttemptAt);
        Assert.Equal(0, await CountOrderApprovedEventsAsync(context, orderId));

        gateway.PaymentToReturn = new("pay-approved", "approved", orderId.ToString(), amount, "ARS");
        var approved = await sut.ExecuteAsync("req-approved", "pay-approved");

        Assert.Equal(ProcessPaymentNotificationOutcome.Approved, approved.Outcome);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(1, await CountOrderApprovedEventsAsync(context, orderId));
    }

    [Fact]
    public async Task Refunded_status_is_ignored_and_writes_no_order_fields()
    {
        var (orderId, amount) = await SeedPendingOrderAsync();
        var gateway = new FakePaymentGateway { PaymentToReturn = new("pay-refunded", "refunded", orderId.ToString(), amount, "ARS") };

        await using var context = _fixture.CreateContext();
        var sut = new ProcessPaymentNotification(context, gateway, NullLogger<ProcessPaymentNotification>.Instance);

        var result = await sut.ExecuteAsync("req-refunded", "pay-refunded");

        Assert.Equal(ProcessPaymentNotificationOutcome.Ignored, result.Outcome);
        Assert.Equal("non_actionable_status", result.Reason);

        var order = await context.Orders.SingleAsync(o => o.Id == orderId);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Null(order.LastPaymentAttemptId);
        Assert.Null(order.MpPaymentId);
        Assert.Equal(orderId, (await context.ProcessedWebhookNotifications.SingleAsync(n => n.RequestId == "req-refunded")).OrderId);
    }

    [Fact]
    public async Task Unknown_external_reference_is_ignored()
    {
        var missingOrderId = Guid.NewGuid();
        var gateway = new FakePaymentGateway { PaymentToReturn = new("pay-unknown", "approved", missingOrderId.ToString(), 100m, "ARS") };

        await using var context = _fixture.CreateContext();
        var sut = new ProcessPaymentNotification(context, gateway, NullLogger<ProcessPaymentNotification>.Instance);

        var result = await sut.ExecuteAsync("req-unknown", "pay-unknown");

        Assert.Equal(ProcessPaymentNotificationOutcome.OrderNotFound, result.Outcome);
        Assert.Equal(
            missingOrderId,
            (await context.ProcessedWebhookNotifications.SingleAsync(n => n.RequestId == "req-unknown")).OrderId);
    }

    /// <summary>Unique per call — <c>orders.mp_payment_id</c> is uniquely indexed and this collection shares one database.</summary>
    private static string UniquePaymentId() => $"pay-{Guid.NewGuid():N}";

    private async Task<(Guid OrderId, decimal Amount)> SeedPendingOrderAsync()
    {
        await using var context = _fixture.CreateContext();
        var order = Order.Create(
            null,
            $"buyer-{Guid.NewGuid():N}@example.com",
            [new OrderLine(Guid.NewGuid(), "Product", "Standard", 2_500m, 2)],
            DateTimeOffset.UtcNow);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return (order.Id, order.TotalAmount);
    }

    /// <summary>Counts <c>OrderApproved</c> rows for <paramref name="orderId"/>; filters the jsonb payload in memory (not server-translatable).</summary>
    private static async Task<int> CountOrderApprovedEventsAsync(AppDbContext context, Guid orderId)
    {
        var events = await context.OutboxEvents.Where(e => e.Type == OutboxEventTypes.OrderApproved).ToListAsync();
        return events.Count(e => e.Payload.Contains(orderId.ToString()));
    }
}
