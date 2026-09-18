using Maxkeys.Application.Fulfillment;
using Maxkeys.Application.Security;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Common;
using Maxkeys.Domain.Orders;
using Maxkeys.Domain.Outbox;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Tests.Fulfillment;

/// <summary>
/// Covers <see cref="RequestDeliveryResend"/> (admin-buyers spec: Resend
/// Delivery Email; fulfillment spec: One-Time Delivery Email — MODIFIED;
/// design D1): a resend for a <c>Delivered</c> order writes exactly one
/// <c>OrderDeliveryResendRequested</c> outbox row carrying the acting admin's
/// sub, without touching <c>DeliveredAt</c> or any key; a resend for a
/// non-<c>Delivered</c> order is rejected and writes no outbox row.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RequestDeliveryResendTests
{
    private const string AdminSub = "22222222-2222-2222-2222-222222222222";

    private static readonly string ValidKeyBase64 =
        Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private readonly PostgresFixture _fixture;

    public RequestDeliveryResendTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Resend_on_delivered_order_does_not_alter_delivered_at_or_keys()
    {
        var orderId = await SeedDeliveredOrderAsync();

        await using var beforeContext = _fixture.CreateContext();
        var before = await beforeContext.Orders.Include(o => o.Items).ThenInclude(i => i.Keys).SingleAsync(o => o.Id == orderId);
        var deliveredAtBefore = before.DeliveredAt;
        var keyCodesBefore = before.Items.SelectMany(i => i.Keys).Select(k => Convert.ToBase64String(k.EncryptedCode)).OrderBy(c => c).ToList();

        await using var context = _fixture.CreateContext();
        var outboxEventId = await CreateSut(context).ExecuteAsync(orderId, AdminSub);

        Assert.NotNull(outboxEventId);

        await using var afterContext = _fixture.CreateContext();
        var after = await afterContext.Orders.Include(o => o.Items).ThenInclude(i => i.Keys).SingleAsync(o => o.Id == orderId);
        Assert.Equal(deliveredAtBefore, after.DeliveredAt);
        Assert.Equal(OrderStatus.Delivered, after.Status);
        var keyCodesAfter = after.Items.SelectMany(i => i.Keys).Select(k => Convert.ToBase64String(k.EncryptedCode)).OrderBy(c => c).ToList();
        Assert.Equal(keyCodesBefore, keyCodesAfter);
    }

    [Fact]
    public async Task Resend_writes_outbox_event_with_requested_by_equal_to_acting_admin_sub()
    {
        var orderId = await SeedDeliveredOrderAsync();

        await using var context = _fixture.CreateContext();
        await CreateSut(context).ExecuteAsync(orderId, AdminSub);

        // jsonb has no LIKE operator, so filter the (small) candidate set by type in SQL and match the payload in memory.
        var candidateEvents = await context.OutboxEvents
            .Where(e => e.Type == OutboxEventTypes.OrderDeliveryResendRequested)
            .ToListAsync();
        var evt = Assert.Single(candidateEvents, e => e.Payload.Contains(orderId.ToString()));
        Assert.Contains($"\"requestedBy\":\"{AdminSub}\"", evt.Payload);
    }

    [Fact]
    public async Task Resend_on_non_delivered_order_is_rejected_and_writes_no_outbox_row()
    {
        var orderId = await SeedAwaitingFulfillmentOrderAsync();

        await using var context = _fixture.CreateContext();

        await Assert.ThrowsAsync<DomainConflictException>(() => CreateSut(context).ExecuteAsync(orderId, AdminSub));

        var candidateEvents = await context.OutboxEvents
            .Where(e => e.Type == OutboxEventTypes.OrderDeliveryResendRequested)
            .ToListAsync();
        Assert.DoesNotContain(candidateEvents, e => e.Payload.Contains(orderId.ToString()));
    }

    private static RequestDeliveryResend CreateSut(AppDbContext context) => new(context);

    private async Task<Guid> SeedDeliveredOrderAsync()
    {
        var cipher = new KeyCipher(Options.Create(new KeyCipherOptions { EncryptionKey = ValidKeyBase64, CurrentVersion = 1 }));
        var now = DateTimeOffset.UtcNow;

        await using var context = _fixture.CreateContext();
        var order = Order.Create(
            null, $"buyer-{Guid.NewGuid():N}@example.com", [new OrderLine(Guid.NewGuid(), "Product", "Standard", 1_000m, 1)], now);
        order.MarkPaid($"pay-{Guid.NewGuid():N}", now);
        order.MarkAwaitingFulfillment(now);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var item = order.Items.Single();
        var (blob, version) = cipher.Encrypt("CODE-ONE");
        var key = new Maxkeys.Domain.Keys.Key(item.ProductVariantId, blob, version, "seed-admin", now);
        context.Keys.Add(key); // client-generated Id — must be added explicitly (see AttachKeyToOrderItem)
        order.AttachKey(item.Id, key, now);
        order.MarkDelivered(now);
        await context.SaveChangesAsync();

        Assert.Equal(OrderStatus.Delivered, order.Status);
        return order.Id;
    }

    private async Task<Guid> SeedAwaitingFulfillmentOrderAsync()
    {
        var now = DateTimeOffset.UtcNow;

        await using var context = _fixture.CreateContext();
        var order = Order.Create(
            null, $"buyer-{Guid.NewGuid():N}@example.com", [new OrderLine(Guid.NewGuid(), "Product", "Standard", 1_000m, 1)], now);
        order.MarkPaid($"pay-{Guid.NewGuid():N}", now);
        order.MarkAwaitingFulfillment(now);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        return order.Id;
    }
}
