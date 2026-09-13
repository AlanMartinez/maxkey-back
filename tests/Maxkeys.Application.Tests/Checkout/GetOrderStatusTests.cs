using Maxkeys.Application.Checkout;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Orders;

namespace Maxkeys.Application.Tests.Checkout;

[Collection(PostgresCollection.Name)]
public sealed class GetOrderStatusTests
{
    private readonly PostgresFixture _fixture;

    public GetOrderStatusTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Pending_order_returns_last_attempt_and_masked_email_only()
    {
        var orderId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using (var seed = _fixture.CreateContext())
        {
            var order = Order.Create(null, "buyer@example.com", [new OrderLine(Guid.NewGuid(), "Product", "Standard", 100m, 1)], now);
            order.RecordPaymentAttempt("payment-123", "pending", now);
            orderId = order.Id;
            seed.Orders.Add(order);
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new GetOrderStatus(context);

        var result = await sut.ExecuteAsync(orderId);

        Assert.NotNull(result);
        Assert.Equal(orderId, result!.OrderId);
        Assert.Equal(OrderStatus.Pending, result.Status);
        Assert.Equal("b***@example.com", result.BuyerEmail);
        Assert.NotEqual("buyer@example.com", result.BuyerEmail);
        Assert.Equal("payment-123", result.LastPaymentAttemptId);
        Assert.Equal("pending", result.LastPaymentAttemptStatus);
        Assert.NotNull(result.LastPaymentAttemptAt);
    }

    [Fact]
    public async Task Unknown_order_returns_null_without_disclosure()
    {
        await using var context = _fixture.CreateContext();
        var sut = new GetOrderStatus(context);

        var result = await sut.ExecuteAsync(Guid.NewGuid());

        Assert.Null(result);
    }
}
