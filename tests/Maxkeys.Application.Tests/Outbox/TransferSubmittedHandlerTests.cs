using Maxkeys.Application.Notifications;
using Maxkeys.Application.Outbox;
using Maxkeys.Application.Tests.Fakes;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Orders;
using Maxkeys.Domain.Outbox;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Tests.Outbox;

[Collection(PostgresCollection.Name)]
public sealed class TransferSubmittedHandlerTests
{
    private readonly PostgresFixture _fixture;

    public TransferSubmittedHandlerTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Order_awaiting_validation_emails_the_operator_with_buyer_contact_and_total()
    {
        var order = await SeedTransferOrderAsync(approve: false);
        var sender = new RecordingEmailSender();

        await HandleAsync(order.Id, sender);

        var email = Assert.Single(sender.SentMessages);
        Assert.Equal("operator@example.com", email.To);
        Assert.Equal($"Order {order.Id} awaiting bank transfer validation", email.Subject);
        Assert.Contains(order.BuyerEmail, email.TextBody);
        Assert.Contains("Phone: +5491112345678", email.TextBody);
        Assert.Contains("Total: 2500", email.TextBody);
    }

    [Fact]
    public async Task Already_reviewed_order_sends_nothing()
    {
        var order = await SeedTransferOrderAsync(approve: true);
        var sender = new RecordingEmailSender();

        await HandleAsync(order.Id, sender);

        Assert.Empty(sender.SentMessages);
    }

    [Fact]
    public async Task Unknown_order_throws_so_the_processor_retries()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => HandleAsync(Guid.NewGuid(), new RecordingEmailSender()));
    }

    private async Task HandleAsync(Guid orderId, RecordingEmailSender sender)
    {
        await using var context = _fixture.CreateContext();
        var handler = new TransferSubmittedHandler(
            context,
            sender,
            Options.Create(new EmailOptions { OperatorTo = "operator@example.com" }),
            NullLogger<TransferSubmittedHandler>.Instance);

        await handler.HandleAsync(
            new OutboxEvent(OutboxEventTypes.TransferSubmitted, $$"""{"orderId":"{{orderId}}"}""", DateTimeOffset.UtcNow),
            CancellationToken.None);
    }

    private async Task<Order> SeedTransferOrderAsync(bool approve)
    {
        var now = DateTimeOffset.UtcNow;
        await using var context = _fixture.CreateContext();
        var order = Order.Create(
            null,
            $"buyer-{Guid.NewGuid():N}@example.com",
            [new OrderLine(Guid.NewGuid(), "Roblox", "AR", 2_500m, 1)],
            now,
            PaymentMethod.BankTransfer,
            "+54 9 11 1234-5678");
        if (approve)
        {
            order.ApproveTransfer(now);
        }

        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order;
    }
}
