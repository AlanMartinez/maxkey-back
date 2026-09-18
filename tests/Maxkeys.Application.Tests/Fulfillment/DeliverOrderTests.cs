using Maxkeys.Application.Fulfillment;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Common;
using Maxkeys.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maxkeys.Application.Tests.Fulfillment;

/// <summary>Covers <see cref="DeliverOrder"/> (admin-key-delivery-gate spec: decision 3, "Entregar").</summary>
[Collection(PostgresCollection.Name)]
public sealed class DeliverOrderTests
{
    private readonly PostgresFixture _fixture;

    public DeliverOrderTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task KeysAssigned_order_transitions_to_delivered()
    {
        var orderId = await SeedKeysAssignedOrderAsync();

        await using var context = _fixture.CreateContext();
        var sut = new DeliverOrder(context, NullLogger<DeliverOrder>.Instance);

        var status = await sut.ExecuteAsync(orderId, "admin@maxkeys.test");

        Assert.Equal(OrderStatus.Delivered, status);

        var order = await context.Orders.SingleAsync(o => o.Id == orderId);
        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.NotNull(order.DeliveredAt);
    }

    [Fact]
    public async Task AwaitingFulfillment_order_throws_conflict()
    {
        var orderId = await SeedAwaitingFulfillmentOrderAsync();

        await using var context = _fixture.CreateContext();
        var sut = new DeliverOrder(context, NullLogger<DeliverOrder>.Instance);

        await Assert.ThrowsAsync<DomainConflictException>(() => sut.ExecuteAsync(orderId, "admin@maxkeys.test"));
    }

    [Fact]
    public async Task Unknown_order_returns_null()
    {
        await using var context = _fixture.CreateContext();
        var sut = new DeliverOrder(context, NullLogger<DeliverOrder>.Instance);

        var status = await sut.ExecuteAsync(Guid.NewGuid(), "admin@maxkeys.test");

        Assert.Null(status);
    }

    private async Task<Guid> SeedKeysAssignedOrderAsync()
    {
        var now = DateTimeOffset.UtcNow;
        await using var context = _fixture.CreateContext();
        var order = Order.Create(null, $"buyer-{Guid.NewGuid():N}@example.com", [new OrderLine(Guid.NewGuid(), "Product", "Standard", 1_000m, 1)], now);
        order.MarkPaid($"pay-{Guid.NewGuid():N}", now);
        order.MarkAwaitingFulfillment(now);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var item = order.Items.Single();
        var key = new Maxkeys.Domain.Keys.Key(item.ProductVariantId, new byte[] { 1, 2, 3 }, 1, "seed-admin", now);
        context.Keys.Add(key);
        order.AttachKey(item.Id, key, now);
        await context.SaveChangesAsync();

        return order.Id;
    }

    private async Task<Guid> SeedAwaitingFulfillmentOrderAsync()
    {
        var now = DateTimeOffset.UtcNow;
        await using var context = _fixture.CreateContext();
        var order = Order.Create(null, $"buyer-{Guid.NewGuid():N}@example.com", [new OrderLine(Guid.NewGuid(), "Product", "Standard", 1_000m, 1)], now);
        order.MarkPaid($"pay-{Guid.NewGuid():N}", now);
        order.MarkAwaitingFulfillment(now);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }
}
