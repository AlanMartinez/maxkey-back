using Maxkeys.Application.Buyers;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Keys;
using Maxkeys.Domain.Orders;
using Maxkeys.Domain.Outbox;
using Maxkeys.Infrastructure.Persistence;

namespace Maxkeys.Application.Tests.Buyers;

/// <summary>
/// Covers <see cref="GetAdminOrderDetail"/> (admin-buyers spec: Order Detail,
/// Key Exposure in Buyer View) against real Postgres — the outbox lookup is a
/// <c>jsonb</c> containment query (ADR-09).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GetAdminOrderDetailTests
{
    private readonly PostgresFixture _fixture;

    public GetAdminOrderDetailTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Delivered_order_returns_scalars_items_keys_and_events_oldest_first()
    {
        var now = DateTimeOffset.UtcNow;
        var email = $"buyer-{Guid.NewGuid():N}@example.com";
        var paymentId = $"pay-{Guid.NewGuid():N}";
        var variantId = Guid.NewGuid();
        Guid orderId;
        Guid itemId;
        Guid revealedKeyId;
        Guid assignedKeyId;

        await using (var seed = _fixture.CreateContext())
        {
            var order = Order.Create(null, email, [new OrderLine(variantId, "Product", "Standard", 1_000m, 2)], now);
            order.MarkPaid(paymentId, now);
            order.MarkAwaitingFulfillment(now);
            seed.Orders.Add(order);
            await seed.SaveChangesAsync();

            var item = order.Items.Single();
            var revealedKey = new Key(variantId, new byte[] { 1, 2, 3 }, 1, "seed-admin", now);
            var assignedKey = new Key(variantId, new byte[] { 4, 5, 6 }, 1, "seed-admin", now);
            seed.Keys.Add(revealedKey); // client-generated Id — must be added explicitly (see AttachKeyToOrderItem)
            seed.Keys.Add(assignedKey);
            order.AttachKey(item.Id, revealedKey, now);
            order.AttachKey(item.Id, assignedKey, now.AddSeconds(1));
            order.MarkDelivered(now);
            revealedKey.Reveal(now.AddMinutes(1), "buyer-sub");

            seed.OutboxEvents.Add(new OutboxEvent(
                OutboxEventTypes.OrderApproved, $$"""{"orderId":"{{order.Id}}"}""", now.AddMinutes(-2)));
            seed.OutboxEvents.Add(new OutboxEvent(
                OutboxEventTypes.OrderDeliveryResendRequested, $$"""{"orderId":"{{order.Id}}"}""", now.AddMinutes(-1)));
            seed.OutboxEvents.Add(new OutboxEvent(
                OutboxEventTypes.OrderApproved, $$"""{"orderId":"{{Guid.NewGuid()}}"}""", now.AddMinutes(-3)));
            await seed.SaveChangesAsync();

            orderId = order.Id;
            itemId = item.Id;
            revealedKeyId = revealedKey.Id;
            assignedKeyId = assignedKey.Id;
        }

        await using var context = _fixture.CreateContext();
        var sut = new GetAdminOrderDetail(context, new OutboxEventReader(context));

        var result = await sut.ExecuteAsync(orderId);

        Assert.NotNull(result);
        Assert.Equal(orderId, result!.Id);
        Assert.Equal(email, result.BuyerEmail);
        Assert.Equal("Delivered", result.Status);
        Assert.Equal(2_000m, result.TotalAmount);
        Assert.Equal("ARS", result.Currency);
        Assert.Equal(paymentId, result.MpPaymentId);
        Assert.NotNull(result.PaidAt);
        Assert.NotNull(result.DeliveredAt);

        var item0 = Assert.Single(result.Items);
        Assert.Equal(itemId, item0.ItemId);
        Assert.Equal("Product", item0.ProductName);
        Assert.Equal("Standard", item0.VariantName);
        Assert.Equal(1_000m, item0.UnitPrice);
        Assert.Equal(2, item0.Quantity);

        Assert.Equal(2, item0.Keys.Count);
        var revealed = Assert.Single(item0.Keys, k => k.KeyId == revealedKeyId);
        Assert.Equal("Revealed", revealed.Status);
        Assert.NotNull(revealed.AssignedAt);
        Assert.NotNull(revealed.RevealedAt);
        Assert.Equal("buyer-sub", revealed.RevealedBy);

        var assigned = Assert.Single(item0.Keys, k => k.KeyId == assignedKeyId);
        Assert.Equal("Assigned", assigned.Status);
        Assert.NotNull(assigned.AssignedAt);
        Assert.Null(assigned.RevealedAt);
        Assert.Null(assigned.RevealedBy);

        Assert.Equal(2, result.Events.Count);
        Assert.Equal(OutboxEventTypes.OrderApproved, result.Events[0].Type);
        Assert.Equal(OutboxEventTypes.OrderDeliveryResendRequested, result.Events[1].Type);
        Assert.True(result.Events[0].CreatedAt < result.Events[1].CreatedAt);
        Assert.All(result.Events, e => Assert.Equal("Pending", e.Status));
    }

    [Fact]
    public async Task Unknown_order_returns_null()
    {
        await using var context = _fixture.CreateContext();
        var sut = new GetAdminOrderDetail(context, new OutboxEventReader(context));

        var result = await sut.ExecuteAsync(Guid.NewGuid());

        Assert.Null(result);
    }
}
