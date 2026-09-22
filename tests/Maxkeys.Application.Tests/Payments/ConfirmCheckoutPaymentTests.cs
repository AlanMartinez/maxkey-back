using Maxkeys.Application.Payments;
using Maxkeys.Application.Tests.Fakes;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Orders;
using Maxkeys.Domain.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maxkeys.Application.Tests.Payments;

[Collection(PostgresCollection.Name)]
public sealed class ConfirmCheckoutPaymentTests
{
    private readonly PostgresFixture _fixture;

    public ConfirmCheckoutPaymentTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Approved_payment_is_persisted_through_the_authoritative_gateway_flow()
    {
        var (orderId, amount) = await SeedOrderAsync();
        var payment = new PaymentInfo($"pay-{Guid.NewGuid():N}", "approved", orderId.ToString(), amount, "ARS");
        var gateway = new FakePaymentGateway { ApprovedPaymentToReturn = payment, PaymentToReturn = payment };

        var result = await ExecuteAsync(orderId, gateway);

        Assert.Equal(ConfirmCheckoutPaymentOutcome.Approved, result.Outcome);
        Assert.Equal([orderId.ToString()], gateway.FindApprovedPaymentCalls);
        Assert.Equal([payment.Id], gateway.GetPaymentCalls);

        await using var readContext = _fixture.CreateContext();
        Assert.Equal(OrderStatus.Paid, (await readContext.Orders.SingleAsync(order => order.Id == orderId)).Status);
        Assert.Equal(1, await CountApprovedEventsAsync(readContext, orderId));
    }

    [Fact]
    public async Task No_approved_payment_keeps_order_pending_for_later_webhook_or_reconciliation()
    {
        var (orderId, _) = await SeedOrderAsync();
        var gateway = new FakePaymentGateway();

        var result = await ExecuteAsync(orderId, gateway);

        Assert.Equal(ConfirmCheckoutPaymentOutcome.Pending, result.Outcome);

        await using var readContext = _fixture.CreateContext();
        Assert.Equal(OrderStatus.Pending, (await readContext.Orders.SingleAsync(order => order.Id == orderId)).Status);
        Assert.Equal(0, await CountApprovedEventsAsync(readContext, orderId));
    }

    [Fact]
    public async Task Repeated_confirmation_is_idempotent_and_creates_one_approved_event()
    {
        var (orderId, amount) = await SeedOrderAsync();
        var payment = new PaymentInfo($"pay-{Guid.NewGuid():N}", "approved", orderId.ToString(), amount, "ARS");
        var gateway = new FakePaymentGateway { ApprovedPaymentToReturn = payment, PaymentToReturn = payment };

        await ExecuteAsync(orderId, gateway);
        var repeated = await ExecuteAsync(orderId, gateway);

        Assert.Equal(ConfirmCheckoutPaymentOutcome.AlreadyProcessed, repeated.Outcome);

        await using var readContext = _fixture.CreateContext();
        Assert.Equal(OrderStatus.Paid, (await readContext.Orders.SingleAsync(order => order.Id == orderId)).Status);
        Assert.Equal(1, await CountApprovedEventsAsync(readContext, orderId));
    }

    private async Task<ConfirmCheckoutPaymentResult> ExecuteAsync(Guid orderId, FakePaymentGateway gateway)
    {
        await using var context = _fixture.CreateContext();
        var process = new ProcessPaymentNotification(context, gateway, NullLogger<ProcessPaymentNotification>.Instance);
        var sut = new ConfirmCheckoutPayment(context, gateway, process);
        return await sut.ExecuteAsync(orderId);
    }

    private async Task<(Guid OrderId, decimal Amount)> SeedOrderAsync()
    {
        await using var context = _fixture.CreateContext();
        var order = Order.Create(null, $"buyer-{Guid.NewGuid():N}@example.com", [new OrderLine(Guid.NewGuid(), "Product", "Standard", 2_500m, 2)], DateTimeOffset.UtcNow);
        order.AttachPreference($"pref-{Guid.NewGuid():N}");
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return (order.Id, order.TotalAmount);
    }

    private static async Task<int> CountApprovedEventsAsync(Maxkeys.Infrastructure.Persistence.AppDbContext context, Guid orderId)
    {
        var events = await context.OutboxEvents.Where(evt => evt.Type == OutboxEventTypes.OrderApproved).ToListAsync();
        return events.Count(evt => evt.Payload.Contains(orderId.ToString()));
    }
}
