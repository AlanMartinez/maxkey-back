using Maxkeys.Application.Orders;
using Maxkeys.Application.Security;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Orders;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Tests.Orders;

/// <summary>
/// Covers <see cref="GetMyOrders"/> and <see cref="GetMyOrder"/> (orders-history
/// spec: My Orders Listing, Order Detail With Conditional Key Reveal, Ownership
/// Enforcement; tasks.md 8.7): owner-only scoping, cross-user 404 (via a
/// <see langword="null"/> result the API layer maps to 404), and keys hidden
/// unless the order is <see cref="OrderStatus.Delivered"/>.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GetMyOrdersTests
{
    private static readonly string ValidKeyBase64 =
        Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private readonly PostgresFixture _fixture;

    public GetMyOrdersTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetMyOrders_returns_only_the_caller_orders()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        await using var seedContext = _fixture.CreateContext();
        await SeedOrderAsync(seedContext, userA);
        await SeedOrderAsync(seedContext, userA);
        await SeedOrderAsync(seedContext, userB);

        await using var context = _fixture.CreateContext();
        var result = await new GetMyOrders(context).ExecuteAsync(userA);

        Assert.Equal(2, result.Count);
        Assert.All(result, order => Assert.Equal(1, order.ItemCount));
    }

    [Fact]
    public async Task GetMyOrders_excludes_pending_and_cancelled_orders()
    {
        var userId = Guid.NewGuid();

        await using var seedContext = _fixture.CreateContext();
        await SeedOrderAsync(seedContext, userId);
        await SeedPendingOrderAsync(seedContext, userId);
        await SeedCancelledOrderAsync(seedContext, userId);

        await using var context = _fixture.CreateContext();
        var result = await new GetMyOrders(context).ExecuteAsync(userId);

        Assert.Single(result);
        Assert.Equal(OrderStatus.AwaitingFulfillment, result[0].Status);
    }

    [Fact]
    public async Task GetMyOrder_returns_null_for_a_non_owner()
    {
        var owner = Guid.NewGuid();
        var stranger = Guid.NewGuid();

        await using var seedContext = _fixture.CreateContext();
        var orderId = await SeedOrderAsync(seedContext, owner);

        await using var context = _fixture.CreateContext();
        var result = await CreateSut(context).ExecuteAsync(orderId, stranger);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetMyOrder_hides_keys_when_not_delivered_and_reveals_them_when_delivered()
    {
        var userId = Guid.NewGuid();

        await using var seedContext = _fixture.CreateContext();
        var awaitingOrderId = await SeedOrderAsync(seedContext, userId, deliver: false);
        var deliveredOrderId = await SeedOrderAsync(seedContext, userId, deliver: true);

        await using var context = _fixture.CreateContext();
        var sut = CreateSut(context);

        var awaiting = await sut.ExecuteAsync(awaitingOrderId, userId);
        var delivered = await sut.ExecuteAsync(deliveredOrderId, userId);

        Assert.NotNull(awaiting);
        Assert.All(awaiting!.Items, item => Assert.Null(item.Keys));

        Assert.NotNull(delivered);
        Assert.All(delivered!.Items, item => Assert.NotEmpty(item.Keys!));
    }

    private static GetMyOrder CreateSut(Infrastructure.Persistence.AppDbContext context) =>
        new(context, new KeyCipher(Options.Create(new KeyCipherOptions { EncryptionKey = ValidKeyBase64, CurrentVersion = 1 })));

    private static async Task<Guid> SeedOrderAsync(Infrastructure.Persistence.AppDbContext context, Guid userId, bool deliver = false)
    {
        var now = DateTimeOffset.UtcNow;
        var order = Order.Create(userId, $"buyer-{Guid.NewGuid():N}@example.com", [new OrderLine(Guid.NewGuid(), "Product", "Standard", 1_000m, 1)], now);
        order.MarkPaid($"pay-{Guid.NewGuid():N}", now);
        order.MarkAwaitingFulfillment(now);

        if (deliver)
        {
            var item = order.Items.Single();
            var cipher = new KeyCipher(Options.Create(new KeyCipherOptions { EncryptionKey = ValidKeyBase64, CurrentVersion = 1 }));
            var (blob, version) = cipher.Encrypt("SEED-CODE");
            order.AttachKey(item.Id, new Maxkeys.Domain.Keys.Key(item.ProductVariantId, blob, version, "seed-admin", now), now);
        }

        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }

    private static async Task SeedPendingOrderAsync(Infrastructure.Persistence.AppDbContext context, Guid userId)
    {
        var now = DateTimeOffset.UtcNow;
        var order = Order.Create(userId, $"buyer-{Guid.NewGuid():N}@example.com", [new OrderLine(Guid.NewGuid(), "Product", "Standard", 1_000m, 1)], now);

        context.Orders.Add(order);
        await context.SaveChangesAsync();
    }

    private static async Task SeedCancelledOrderAsync(Infrastructure.Persistence.AppDbContext context, Guid userId)
    {
        var now = DateTimeOffset.UtcNow;
        var order = Order.Create(userId, $"buyer-{Guid.NewGuid():N}@example.com", [new OrderLine(Guid.NewGuid(), "Product", "Standard", 1_000m, 1)], now);
        order.Cancel(now);

        context.Orders.Add(order);
        await context.SaveChangesAsync();
    }
}
