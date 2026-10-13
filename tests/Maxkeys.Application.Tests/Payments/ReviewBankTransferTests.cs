using Maxkeys.Application.Fulfillment;
using Maxkeys.Application.Notifications;
using Maxkeys.Application.Outbox;
using Maxkeys.Application.Payments;
using Maxkeys.Application.Tests.Fakes;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Common;
using Maxkeys.Domain.Orders;
using Maxkeys.Domain.Outbox;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Tests.Payments;

/// <summary>Covers <see cref="ApproveBankTransfer"/> and <see cref="RejectBankTransfer"/>.</summary>
[Collection(PostgresCollection.Name)]
public sealed class ReviewBankTransferTests
{
    private readonly PostgresFixture _fixture;

    public ReviewBankTransferTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Approve_marks_paid_and_enqueues_order_approved_like_the_mercado_pago_path()
    {
        var orderId = await SeedTransferOrderAsync();

        await using (var context = _fixture.CreateContext())
        {
            var summary = await new ApproveBankTransfer(context, NullLogger<ApproveBankTransfer>.Instance)
                .ExecuteAsync(orderId, "admin-sub");

            Assert.NotNull(summary);
            Assert.Equal(OrderStatus.Paid, summary.Status);
            Assert.Equal(PaymentMethod.BankTransfer, summary.PaymentMethod);
            Assert.Equal("1112345678", summary.BuyerPhone);
        }

        await using var readContext = _fixture.CreateContext();
        var order = await readContext.Orders.SingleAsync(o => o.Id == orderId);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.NotNull(order.PaidAt);
        // OutboxEventReader matches on the {"orderId": ...} payload the OrderApproved handler parses.
        var evt = Assert.Single(await OutboxEventsForAsync(readContext, orderId));
        Assert.Equal(OutboxEventTypes.OrderApproved, evt.Type);
    }

    [Fact]
    public async Task Approved_transfer_is_picked_up_by_the_existing_order_approved_handler()
    {
        var orderId = await SeedTransferOrderAsync();
        await using (var context = _fixture.CreateContext())
        {
            await new ApproveBankTransfer(context, NullLogger<ApproveBankTransfer>.Instance).ExecuteAsync(orderId, "admin-sub");
        }

        var sender = new RecordingEmailSender();
        await using (var context = _fixture.CreateContext())
        {
            var evt = Assert.Single(await OutboxEventsForAsync(context, orderId));
            var handler = new OrderApprovedHandler(
                context,
                new AssignVaultKeysToOrder(context),
                sender,
                Options.Create(new EmailOptions { OperatorTo = "operator@example.com" }),
                NullLogger<OrderApprovedHandler>.Instance);
            await handler.HandleAsync(evt, CancellationToken.None);
        }

        await using var readContext = _fixture.CreateContext();
        Assert.Equal(OrderStatus.AwaitingFulfillment, (await readContext.Orders.SingleAsync(o => o.Id == orderId)).Status);
        Assert.Equal($"Order {orderId} awaiting fulfillment", Assert.Single(sender.SentMessages).Subject);
    }

    [Fact]
    public async Task Reject_cancels_without_enqueuing_events()
    {
        var orderId = await SeedTransferOrderAsync();

        await using (var context = _fixture.CreateContext())
        {
            var summary = await new RejectBankTransfer(context, NullLogger<RejectBankTransfer>.Instance)
                .ExecuteAsync(orderId, "admin-sub");

            Assert.Equal(OrderStatus.Cancelled, summary!.Status);
        }

        await using var readContext = _fixture.CreateContext();
        Assert.Equal(OrderStatus.Cancelled, (await readContext.Orders.SingleAsync(o => o.Id == orderId)).Status);
        Assert.Empty(await OutboxEventsForAsync(readContext, orderId));
    }

    [Fact]
    public async Task Unknown_order_returns_null()
    {
        await using var context = _fixture.CreateContext();

        Assert.Null(await new ApproveBankTransfer(context, NullLogger<ApproveBankTransfer>.Instance).ExecuteAsync(Guid.NewGuid(), "admin-sub"));
        Assert.Null(await new RejectBankTransfer(context, NullLogger<RejectBankTransfer>.Instance).ExecuteAsync(Guid.NewGuid(), "admin-sub"));
    }

    [Fact]
    public async Task Second_review_conflicts_and_enqueues_nothing_more()
    {
        var orderId = await SeedTransferOrderAsync();
        await using (var context = _fixture.CreateContext())
        {
            await new ApproveBankTransfer(context, NullLogger<ApproveBankTransfer>.Instance).ExecuteAsync(orderId, "admin-sub");
        }

        await using (var context = _fixture.CreateContext())
        {
            await Assert.ThrowsAsync<DomainConflictException>(() =>
                new ApproveBankTransfer(context, NullLogger<ApproveBankTransfer>.Instance).ExecuteAsync(orderId, "admin-sub"));
            await Assert.ThrowsAsync<DomainConflictException>(() =>
                new RejectBankTransfer(context, NullLogger<RejectBankTransfer>.Instance).ExecuteAsync(orderId, "admin-sub"));
        }

        await using var readContext = _fixture.CreateContext();
        Assert.Single(await OutboxEventsForAsync(readContext, orderId));
    }

    private static Task<IReadOnlyList<OutboxEvent>> OutboxEventsForAsync(AppDbContext context, Guid orderId) =>
        new OutboxEventReader(context).ListByOrderIdAsync(orderId);

    private async Task<Guid> SeedTransferOrderAsync()
    {
        await using var context = _fixture.CreateContext();
        var order = Order.Create(
            null,
            $"buyer-{Guid.NewGuid():N}@example.com",
            [new OrderLine(Guid.NewGuid(), "Product", "Standard", 1_000m, 1)],
            DateTimeOffset.UtcNow,
            PaymentMethod.BankTransfer,
            "11 1234-5678");
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }
}
