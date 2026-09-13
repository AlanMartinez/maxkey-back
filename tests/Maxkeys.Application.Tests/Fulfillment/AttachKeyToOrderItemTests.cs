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
/// Covers <see cref="AttachKeyToOrderItem"/> (fulfillment spec: Key Attachment,
/// All-or-Nothing Delivery Derivation; tasks.md 8.7): over-quantity attach is
/// rejected with 409 even while the order stays awaiting fulfillment; two
/// concurrent attaches on the same last-key item yield exactly one winner; the
/// last key completes delivery and inserts exactly one <c>OrderDelivered</c> row.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AttachKeyToOrderItemTests
{
    private static readonly string ValidKeyBase64 =
        Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private readonly PostgresFixture _fixture;

    public AttachKeyToOrderItemTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Over_quantity_attach_is_rejected_while_order_stays_awaiting_fulfillment()
    {
        var (orderId, _, completeItemId) = await SeedAwaitingFulfillmentOrderAsync(completeQuantity: 1, incompleteQuantity: 1);

        await using var context = _fixture.CreateContext();
        var sut = CreateSut(context);

        var ex = await Assert.ThrowsAsync<DomainConflictException>(
            () => sut.ExecuteAsync(orderId, completeItemId!.Value, "EXTRA-CODE", "admin@maxkeys.test"));
        Assert.Contains("already has all required keys", ex.Message);

        var order = await context.Orders.Include(o => o.Items).ThenInclude(i => i.Keys).SingleAsync(o => o.Id == orderId);
        Assert.Equal(OrderStatus.AwaitingFulfillment, order.Status);
    }

    [Fact]
    public async Task Concurrent_attach_on_the_last_slot_yields_exactly_one_winner()
    {
        var (orderId, itemId, _) = await SeedAwaitingFulfillmentOrderAsync(completeQuantity: null, incompleteQuantity: 2, assignedOnIncomplete: 1);

        await using var contextA = _fixture.CreateContext();
        await using var contextB = _fixture.CreateContext();
        var sutA = CreateSut(contextA);
        var sutB = CreateSut(contextB);

        var tasks = new[]
        {
            RunSafely(() => sutA.ExecuteAsync(orderId, itemId, "CODE-A", "admin-a")),
            RunSafely(() => sutB.ExecuteAsync(orderId, itemId, "CODE-B", "admin-b")),
        };
        var results = await Task.WhenAll(tasks);

        Assert.Single(results, r => r.Result is not null);
        Assert.Single(results, r => r.Error is DomainConflictException);

        await using var readContext = _fixture.CreateContext();
        var order = await readContext.Orders.SingleAsync(o => o.Id == orderId);
        Assert.Equal(OrderStatus.Delivered, order.Status);
    }

    [Fact]
    public async Task Last_key_completes_delivery_and_inserts_exactly_one_order_delivered_event()
    {
        var (orderId, itemId, _) = await SeedAwaitingFulfillmentOrderAsync(completeQuantity: null, incompleteQuantity: 1);

        await using var context = _fixture.CreateContext();
        var sut = CreateSut(context);

        var result = await sut.ExecuteAsync(orderId, itemId, "FINAL-CODE", "admin@maxkeys.test");

        Assert.NotNull(result);
        Assert.Equal(OrderStatus.Delivered, result!.OrderStatus);

        var order = await context.Orders.SingleAsync(o => o.Id == orderId);
        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.NotNull(order.DeliveredAt);

        // jsonb has no LIKE operator, so filter the (small) candidate set by type in SQL and match the payload in memory.
        var candidateEvents = await context.OutboxEvents
            .Where(e => e.Type == OutboxEventTypes.OrderDelivered)
            .ToListAsync();
        Assert.Single(candidateEvents, e => e.Payload.Contains(orderId.ToString()));
    }

    [Fact]
    public async Task Unknown_order_returns_null()
    {
        await using var context = _fixture.CreateContext();
        var sut = CreateSut(context);

        var result = await sut.ExecuteAsync(Guid.NewGuid(), Guid.NewGuid(), "CODE", "admin@maxkeys.test");

        Assert.Null(result);
    }

    private static AttachKeyToOrderItem CreateSut(AppDbContext context) =>
        new(context, new KeyCipher(Options.Create(new KeyCipherOptions { EncryptionKey = ValidKeyBase64, CurrentVersion = 1 })));

    private static async Task<(AttachKeyToOrderItemResult? Result, Exception? Error)> RunSafely(
        Func<Task<AttachKeyToOrderItemResult?>> action)
    {
        try
        {
            return (await action(), null);
        }
        catch (Exception ex)
        {
            return (null, ex);
        }
    }

    /// <summary>
    /// Seeds a `Paid` → `AwaitingFulfillment` order. When <paramref name="completeQuantity"/> is set,
    /// adds a fully-keyed item of that quantity first. Always adds one incomplete item of
    /// <paramref name="incompleteQuantity"/>, pre-assigning <paramref name="assignedOnIncomplete"/> keys to it.
    /// Returns the order id, the incomplete item's id, and the complete item's id (or <see langword="null"/>).
    /// </summary>
    private async Task<(Guid OrderId, Guid IncompleteItemId, Guid? CompleteItemId)> SeedAwaitingFulfillmentOrderAsync(
        int? completeQuantity, int incompleteQuantity, int assignedOnIncomplete = 0)
    {
        var now = DateTimeOffset.UtcNow;

        var lines = new List<OrderLine> { new(Guid.NewGuid(), "Product", "Standard", 1_000m, incompleteQuantity) };
        if (completeQuantity is { } qty)
        {
            lines.Insert(0, new OrderLine(Guid.NewGuid(), "Product Complete", "Standard", 1_000m, qty));
        }

        var order = Order.Create(null, $"buyer-{Guid.NewGuid():N}@example.com", lines, now);
        order.MarkPaid($"pay-{Guid.NewGuid():N}", now);
        order.MarkAwaitingFulfillment(now);

        Guid? completeItemId = completeQuantity is not null
            ? order.Items.First(i => i.ProductNameSnapshot == "Product Complete").Id
            : null;
        var incompleteItemId = order.Items.First(i => i.ProductNameSnapshot == "Product").Id;

        await using (var seedContext = _fixture.CreateContext())
        {
            seedContext.Orders.Add(order);
            await seedContext.SaveChangesAsync();
        }

        // Each pre-attach uses its own fresh context — mirroring the one-DbContext-per-admin-request
        // production shape and avoiding stale in-memory navigation state across successive attaches.
        if (completeQuantity is { } completeQty)
        {
            for (var i = 0; i < completeQty; i++)
            {
                await using var attachContext = _fixture.CreateContext();
                await CreateSut(attachContext).ExecuteAsync(order.Id, completeItemId!.Value, $"SEED-COMPLETE-{i}", "seed-admin");
            }
        }

        for (var i = 0; i < assignedOnIncomplete; i++)
        {
            await using var attachContext = _fixture.CreateContext();
            await CreateSut(attachContext).ExecuteAsync(order.Id, incompleteItemId, $"SEED-PARTIAL-{i}", "seed-admin");
        }

        return (order.Id, incompleteItemId, completeItemId);
    }
}
