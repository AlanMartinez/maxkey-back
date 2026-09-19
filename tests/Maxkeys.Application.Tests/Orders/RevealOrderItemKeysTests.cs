using Maxkeys.Application.Orders;
using Maxkeys.Application.Security;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Common;
using Maxkeys.Domain.Keys;
using Maxkeys.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Tests.Orders;

/// <summary>Covers <see cref="RevealOrderItemKeys"/> (admin-key-delivery-gate spec: decision 2/4, buyer "revelar key").</summary>
[Collection(PostgresCollection.Name)]
public sealed class RevealOrderItemKeysTests
{
    private static readonly string ValidKeyBase64 =
        Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private readonly PostgresFixture _fixture;

    public RevealOrderItemKeysTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Delivered_order_reveals_every_assigned_key_for_that_item()
    {
        var userId = Guid.NewGuid();
        var (orderId, itemId) = await SeedDeliveredOrderAsync(userId, quantity: 2, codes: ["CODE-A", "CODE-B"]);

        await using var context = _fixture.CreateContext();
        var codes = await CreateSut(context).ExecuteAsync(orderId, itemId, userId);

        Assert.NotNull(codes);
        Assert.Equal(new[] { "CODE-A", "CODE-B" }, codes!.OrderBy(c => c));

        var item = await context.Orders.Include(o => o.Items).ThenInclude(i => i.Keys)
            .SelectMany(o => o.Items).SingleAsync(i => i.Id == itemId);
        Assert.All(item.Keys, k =>
        {
            Assert.Equal(KeyStatus.Revealed, k.Status);
            Assert.NotNull(k.RevealedAt);
            Assert.Equal(userId.ToString(), k.RevealedBy);
        });
    }

    [Fact]
    public async Task Revealing_twice_returns_the_same_codes_and_does_not_re_mutate()
    {
        var userId = Guid.NewGuid();
        var (orderId, itemId) = await SeedDeliveredOrderAsync(userId, quantity: 1, codes: ["ONLY-CODE"]);

        await using var context = _fixture.CreateContext();
        var sut = CreateSut(context);

        var first = await sut.ExecuteAsync(orderId, itemId, userId);
        var second = await sut.ExecuteAsync(orderId, itemId, userId);

        Assert.Equal(["ONLY-CODE"], first);
        Assert.Equal(["ONLY-CODE"], second);
    }

    [Fact]
    public async Task Non_owner_returns_null()
    {
        var owner = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var (orderId, itemId) = await SeedDeliveredOrderAsync(owner, quantity: 1, codes: ["CODE"]);

        await using var context = _fixture.CreateContext();
        var codes = await CreateSut(context).ExecuteAsync(orderId, itemId, stranger);

        Assert.Null(codes);
    }

    [Fact]
    public async Task Unknown_order_returns_null()
    {
        await using var context = _fixture.CreateContext();
        var codes = await CreateSut(context).ExecuteAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        Assert.Null(codes);
    }

    [Fact]
    public async Task Order_not_yet_delivered_throws_conflict()
    {
        var userId = Guid.NewGuid();
        var (orderId, itemId) = await SeedKeysAssignedOrderAsync(userId);

        await using var context = _fixture.CreateContext();
        await Assert.ThrowsAsync<DomainConflictException>(() => CreateSut(context).ExecuteAsync(orderId, itemId, userId));
    }

    [Fact]
    public async Task Item_from_another_order_throws()
    {
        var userId = Guid.NewGuid();
        var (orderId, _) = await SeedDeliveredOrderAsync(userId, quantity: 1, codes: ["CODE"]);
        var (_, otherItemId) = await SeedDeliveredOrderAsync(userId, quantity: 1, codes: ["OTHER"]);

        await using var context = _fixture.CreateContext();
        await Assert.ThrowsAsync<DomainException>(() => CreateSut(context).ExecuteAsync(orderId, otherItemId, userId));
    }

    [Fact]
    public async Task Concurrent_reveal_of_the_same_item_never_throws_and_both_calls_return_the_same_codes()
    {
        var userId = Guid.NewGuid();
        var (orderId, itemId) = await SeedDeliveredOrderAsync(userId, quantity: 2, codes: ["CODE-A", "CODE-B"]);

        await using var contextA = _fixture.CreateContext();
        await using var contextB = _fixture.CreateContext();
        var sutA = CreateSut(contextA);
        var sutB = CreateSut(contextB);

        var results = await Task.WhenAll(
            sutA.ExecuteAsync(orderId, itemId, userId),
            sutB.ExecuteAsync(orderId, itemId, userId));

        Assert.NotNull(results[0]);
        Assert.NotNull(results[1]);
        Assert.Equal(results[0]!.OrderBy(c => c), results[1]!.OrderBy(c => c));
        Assert.Equal(new[] { "CODE-A", "CODE-B" }, results[0]!.OrderBy(c => c));
    }

    private static RevealOrderItemKeys CreateSut(Infrastructure.Persistence.AppDbContext context) =>
        new(context, new KeyCipher(Options.Create(new KeyCipherOptions { EncryptionKey = ValidKeyBase64, CurrentVersion = 1 })), NullLogger<RevealOrderItemKeys>.Instance);

    private async Task<(Guid OrderId, Guid ItemId)> SeedKeysAssignedOrderAsync(Guid userId, int quantity = 1, string[]? codes = null)
    {
        codes ??= ["SEED-CODE"];
        var cipher = new KeyCipher(Options.Create(new KeyCipherOptions { EncryptionKey = ValidKeyBase64, CurrentVersion = 1 }));
        var now = DateTimeOffset.UtcNow;

        await using var context = _fixture.CreateContext();
        var order = Order.Create(userId, $"buyer-{Guid.NewGuid():N}@example.com", [new OrderLine(Guid.NewGuid(), "Product", "Standard", 1_000m, quantity)], now);
        order.MarkPaid($"pay-{Guid.NewGuid():N}", now);
        order.MarkAwaitingFulfillment(now);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var item = order.Items.Single();
        foreach (var code in codes)
        {
            var (blob, version) = cipher.Encrypt(code);
            var key = new Maxkeys.Domain.Keys.Key(item.ProductVariantId, blob, version, "seed-admin", now);
            context.Keys.Add(key);
            order.AttachKey(item.Id, key, now);
        }
        await context.SaveChangesAsync();

        return (order.Id, item.Id);
    }

    private async Task<(Guid OrderId, Guid ItemId)> SeedDeliveredOrderAsync(Guid userId, int quantity, string[] codes)
    {
        var (orderId, itemId) = await SeedKeysAssignedOrderAsync(userId, quantity, codes);

        await using var context = _fixture.CreateContext();
        var order = await context.Orders.SingleAsync(o => o.Id == orderId);
        order.MarkDelivered(DateTimeOffset.UtcNow);
        await context.SaveChangesAsync();

        return (orderId, itemId);
    }
}
