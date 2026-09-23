using Maxkeys.Application.Notifications;
using Maxkeys.Application.Outbox;
using Maxkeys.Application.Tests.Fakes;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Orders;
using Maxkeys.Domain.Outbox;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Tests.Outbox;

[Collection(PostgresCollection.Name)]
public sealed class OrderDeliveredHandlerTests
{
    private readonly PostgresFixture _fixture;

    public OrderDeliveredHandlerTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Delivered_order_with_items_sends_its_delivery_email()
    {
        var orderId = await SeedDeliveredOrderAsync();
        var sender = new RecordingEmailSender();

        await using var context = _fixture.CreateContext();
        var handler = new OrderDeliveredHandler(
            context,
            sender,
            Options.Create(new EmailOptions { AccountOrdersUrl = "https://chekeys.com/account/orders" }));

        await handler.HandleAsync(new OutboxEvent(
            OutboxEventTypes.OrderDelivered,
            $$"""{"orderId":"{{orderId}}"}""",
            DateTimeOffset.UtcNow),
            CancellationToken.None);

        var email = Assert.Single(sender.SentMessages);
        Assert.Equal("Tus claves de Roblox ya están listas", email.Subject);
        Assert.Contains("Roblox (AR) x1", email.TextBody);
    }

    private async Task<Guid> SeedDeliveredOrderAsync()
    {
        var now = DateTimeOffset.UtcNow;
        await using var context = _fixture.CreateContext();
        var order = Order.Create(
            null,
            "buyer@example.com",
            [new OrderLine(Guid.NewGuid(), "Roblox", "AR", 1_000m, 1)],
            now);
        order.MarkPaid("pay-1", now);
        order.MarkAwaitingFulfillment(now);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var item = order.Items.Single();
        var key = new Maxkeys.Domain.Keys.Key(item.ProductVariantId, [1, 2, 3], 1, "seed-admin", now);
        context.Keys.Add(key);
        order.AttachKey(item.Id, key, now);
        order.MarkDelivered(now);
        await context.SaveChangesAsync();

        return order.Id;
    }
}
