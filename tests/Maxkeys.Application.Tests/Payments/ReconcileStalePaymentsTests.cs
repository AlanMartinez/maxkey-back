using Maxkeys.Application.Payments;
using Maxkeys.Application.Tests.Fakes;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Orders;
using Maxkeys.Domain.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maxkeys.Application.Tests.Payments;

[Collection(PostgresCollection.Name)]
public sealed class ReconcileStalePaymentsTests
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);

    private readonly PostgresFixture _fixture;

    public ReconcileStalePaymentsTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Stale_order_with_missed_approval_is_reconciled_through_the_webhook_pipeline()
    {
        var (orderId, amount) = await SeedOrderAsync(createdAt: DateTimeOffset.UtcNow.AddMinutes(-30), attachPreference: true);
        var paymentId = $"pay-{Guid.NewGuid():N}";
        var payment = new PaymentInfo(paymentId, "approved", orderId.ToString(), amount, "ARS");
        // ReconcileStalePayments finds the payment via search, then replays it through
        // ProcessPaymentNotification, which always re-fetches by id (authoritative fetch) —
        // both must be configured, exactly like the real MercadoPagoGateway's two calls.
        var gateway = new FakePaymentGateway { ApprovedPaymentToReturn = payment, PaymentToReturn = payment };

        var result = await ExecuteAsync(gateway);

        Assert.Equal(1, result.ScannedCount);
        Assert.Equal(1, result.ReconciledCount);
        Assert.Equal(0, result.StillPendingCount);
        Assert.Equal([orderId.ToString()], gateway.FindApprovedPaymentCalls);

        await using var readContext = _fixture.CreateContext();
        Assert.Equal(OrderStatus.Paid, (await readContext.Orders.SingleAsync(o => o.Id == orderId)).Status);
        Assert.Equal(1, await CountOrderApprovedEventsAsync(readContext, orderId));
    }

    [Fact]
    public async Task Stale_order_with_no_approved_payment_stays_pending()
    {
        await SeedOrderAsync(createdAt: DateTimeOffset.UtcNow.AddMinutes(-30), attachPreference: true);
        var gateway = new FakePaymentGateway { ApprovedPaymentToReturn = null };

        var result = await ExecuteAsync(gateway);

        Assert.Equal(1, result.ScannedCount);
        Assert.Equal(0, result.ReconciledCount);
        Assert.Equal(1, result.StillPendingCount);
    }

    [Fact]
    public async Task Recent_pending_order_is_not_scanned_yet()
    {
        await SeedOrderAsync(createdAt: DateTimeOffset.UtcNow.AddMinutes(-1), attachPreference: true);
        var gateway = new FakePaymentGateway();

        var result = await ExecuteAsync(gateway);

        Assert.Equal(0, result.ScannedCount);
        Assert.Empty(gateway.FindApprovedPaymentCalls);
    }

    [Fact]
    public async Task Stale_order_without_a_preference_is_skipped()
    {
        await SeedOrderAsync(createdAt: DateTimeOffset.UtcNow.AddMinutes(-30), attachPreference: false);
        var gateway = new FakePaymentGateway();

        var result = await ExecuteAsync(gateway);

        Assert.Equal(0, result.ScannedCount);
        Assert.Empty(gateway.FindApprovedPaymentCalls);
    }

    private async Task<ReconcileStalePaymentsResult> ExecuteAsync(FakePaymentGateway gateway)
    {
        await using var context = _fixture.CreateContext();
        var processPaymentNotification = new ProcessPaymentNotification(context, gateway, NullLogger<ProcessPaymentNotification>.Instance);
        var sut = new ReconcileStalePayments(context, gateway, processPaymentNotification, NullLogger<ReconcileStalePayments>.Instance);

        return await sut.ExecuteAsync(StaleAfter, DateTimeOffset.UtcNow);
    }

    private async Task<(Guid OrderId, decimal Amount)> SeedOrderAsync(DateTimeOffset createdAt, bool attachPreference)
    {
        await using var context = _fixture.CreateContext();
        var order = Order.Create(
            null,
            $"buyer-{Guid.NewGuid():N}@example.com",
            [new OrderLine(Guid.NewGuid(), "Product", "Standard", 2_500m, 2)],
            createdAt);

        if (attachPreference)
        {
            order.AttachPreference($"pref-{Guid.NewGuid():N}");
        }

        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return (order.Id, order.TotalAmount);
    }

    /// <summary>Counts <c>OrderApproved</c> rows for <paramref name="orderId"/>; filters the jsonb payload in memory (not server-translatable).</summary>
    private static async Task<int> CountOrderApprovedEventsAsync(Maxkeys.Infrastructure.Persistence.AppDbContext context, Guid orderId)
    {
        var events = await context.OutboxEvents.Where(e => e.Type == OutboxEventTypes.OrderApproved).ToListAsync();
        return events.Count(e => e.Payload.Contains(orderId.ToString()));
    }
}
