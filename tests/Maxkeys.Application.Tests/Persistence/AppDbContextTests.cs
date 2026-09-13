using System.Text.Json;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Catalog;
using Maxkeys.Domain.Keys;
using Maxkeys.Domain.Orders;
using Maxkeys.Domain.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Tests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class AppDbContextTests
{
    private readonly PostgresFixture _fixture;

    public AppDbContextTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Migration_applies_cleanly_and_context_can_query()
    {
        await using var context = _fixture.CreateContext();

        var count = await context.Products.CountAsync();

        Assert.True(count >= 0);
    }

    [Fact]
    public async Task Duplicate_product_slug_throws_DbUpdateException()
    {
        var slug = $"dup-slug-{Guid.NewGuid():N}";

        await using (var context = _fixture.CreateContext())
        {
            context.Products.Add(new Product(slug, "First", "steam"));
            await context.SaveChangesAsync();
        }

        await using var duplicateContext = _fixture.CreateContext();
        duplicateContext.Products.Add(new Product(slug, "Second", "steam"));

        await Assert.ThrowsAsync<DbUpdateException>(() => duplicateContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Duplicate_mp_payment_id_throws_DbUpdateException()
    {
        var now = DateTimeOffset.UtcNow;
        var paymentId = $"pay-{Guid.NewGuid():N}";
        var variantId = Guid.NewGuid();

        Order MakeApprovedOrder(string email) =>
            MarkApproved(Order.Create(null, email, [new OrderLine(variantId, "Product", "Variant", 100m, 1)], now), paymentId, now);

        await using (var context = _fixture.CreateContext())
        {
            context.Orders.Add(MakeApprovedOrder("buyer-a@example.com"));
            await context.SaveChangesAsync();
        }

        await using var duplicateContext = _fixture.CreateContext();
        duplicateContext.Orders.Add(MakeApprovedOrder("buyer-b@example.com"));

        await Assert.ThrowsAsync<DbUpdateException>(() => duplicateContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Order_with_items_and_key_round_trips_through_the_database()
    {
        var now = DateTimeOffset.UtcNow;
        var variantId = Guid.NewGuid();
        var encryptedCode = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var orderId = Guid.Empty;

        await using (var context = _fixture.CreateContext())
        {
            var order = Order.Create(null, "buyer@example.com", [new OrderLine(variantId, "Product", "Variant", 250m, 1)], now);
            order.MarkPaid($"pay-{Guid.NewGuid():N}", now);
            order.MarkAwaitingFulfillment(now);

            var key = new Key(variantId, encryptedCode, 3, "operator@example.com", now);
            var delivered = order.AttachKey(order.Items[0].Id, key, now);
            Assert.True(delivered);

            orderId = order.Id;
            context.Orders.Add(order);
            await context.SaveChangesAsync();
        }

        await using var readContext = _fixture.CreateContext();
        var reloaded = await readContext.Orders
            .Include(o => o.Items)
            .ThenInclude(i => i.Keys)
            .SingleAsync(o => o.Id == orderId);

        Assert.Equal(OrderStatus.Delivered, reloaded.Status);
        var reloadedKey = Assert.Single(Assert.Single(reloaded.Items).Keys);
        Assert.Equal(encryptedCode, reloadedKey.EncryptedCode);
        Assert.Equal(KeyStatus.Assigned, reloadedKey.Status);
    }

    [Fact]
    public async Task OutboxEvent_payload_round_trips_as_jsonb()
    {
        var now = DateTimeOffset.UtcNow;
        var payload = """{"orderId":"11111111-1111-1111-1111-111111111111","amount":250.00}""";
        Guid eventId;

        await using (var context = _fixture.CreateContext())
        {
            var outboxEvent = new OutboxEvent(OutboxEventTypes.OrderApproved, payload, now);
            eventId = outboxEvent.Id;
            context.OutboxEvents.Add(outboxEvent);
            await context.SaveChangesAsync();
        }

        await using var readContext = _fixture.CreateContext();
        var reloaded = await readContext.OutboxEvents.SingleAsync(e => e.Id == eventId);

        // jsonb round-trips semantically, not byte-for-byte (Postgres reformats/reorders keys).
        using var expected = JsonDocument.Parse(payload);
        using var actual = JsonDocument.Parse(reloaded.Payload);
        Assert.Equal(expected.RootElement.GetProperty("orderId").GetString(), actual.RootElement.GetProperty("orderId").GetString());
        Assert.Equal(expected.RootElement.GetProperty("amount").GetDecimal(), actual.RootElement.GetProperty("amount").GetDecimal());
    }

    private static Order MarkApproved(Order order, string paymentId, DateTimeOffset now)
    {
        order.MarkPaid(paymentId, now);
        return order;
    }
}
