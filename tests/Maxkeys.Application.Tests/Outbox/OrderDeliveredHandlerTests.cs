using Maxkeys.Application.Outbox;
using Maxkeys.Application.Security;
using Maxkeys.Application.Tests.Fakes;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Keys;
using Maxkeys.Domain.Orders;
using Maxkeys.Domain.Outbox;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Tests.Outbox;

/// <summary>
/// Covers <see cref="OrderDeliveredHandler"/> (fulfillment spec: One-Time
/// Delivery Email; tasks.md 8.7): the buyer email lists every assigned key
/// exactly once grouped by item; re-invoking the handler with an already
/// <see cref="OutboxEventStatus.Processed"/> row is a no-op; an unknown order
/// throws so the outbox processor can retry/dead-letter.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OrderDeliveredHandlerTests
{
    private static readonly string ValidKeyBase64 =
        Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private readonly PostgresFixture _fixture;

    public OrderDeliveredHandlerTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Delivered_order_sends_one_email_with_every_key_once()
    {
        var (orderId, buyerEmail, codes) = await SeedDeliveredOrderAsync();
        var emailSender = new RecordingEmailSender();

        await using var context = _fixture.CreateContext();
        await CreateHandler(context, emailSender).HandleAsync(OrderDeliveredEvent(orderId), CancellationToken.None);

        var email = Assert.Single(emailSender.SentMessages);
        Assert.Equal(buyerEmail, email.To);
        foreach (var code in codes)
        {
            Assert.Contains(code, email.TextBody);
        }
    }

    [Fact]
    public async Task Reevaluating_an_already_processed_event_sends_no_second_email()
    {
        var (orderId, _, _) = await SeedDeliveredOrderAsync();
        var emailSender = new RecordingEmailSender();
        var evt = OrderDeliveredEvent(orderId);

        await using var context = _fixture.CreateContext();
        await CreateHandler(context, emailSender).HandleAsync(evt, CancellationToken.None);
        Assert.Single(emailSender.SentMessages);

        evt.MarkProcessed(DateTimeOffset.UtcNow);
        await CreateHandler(context, emailSender).HandleAsync(evt, CancellationToken.None);

        Assert.Single(emailSender.SentMessages);
    }

    [Fact]
    public async Task Unknown_order_throws()
    {
        var emailSender = new RecordingEmailSender();
        await using var context = _fixture.CreateContext();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateHandler(context, emailSender).HandleAsync(OrderDeliveredEvent(Guid.NewGuid()), CancellationToken.None));

        Assert.Empty(emailSender.SentMessages);
    }

    private static OrderDeliveredHandler CreateHandler(AppDbContext context, RecordingEmailSender emailSender) =>
        new(context, emailSender, new KeyCipher(Options.Create(new KeyCipherOptions { EncryptionKey = ValidKeyBase64, CurrentVersion = 1 })), NullLogger<OrderDeliveredHandler>.Instance);

    private static OutboxEvent OrderDeliveredEvent(Guid orderId) =>
        new(OutboxEventTypes.OrderDelivered, $$"""{"orderId":"{{orderId}}"}""", DateTimeOffset.UtcNow);

    private async Task<(Guid OrderId, string BuyerEmail, IReadOnlyList<string> Codes)> SeedDeliveredOrderAsync()
    {
        var cipher = new KeyCipher(Options.Create(new KeyCipherOptions { EncryptionKey = ValidKeyBase64, CurrentVersion = 1 }));
        var now = DateTimeOffset.UtcNow;
        var buyerEmail = $"buyer-{Guid.NewGuid():N}@example.com";
        var codes = new[] { "CODE-ONE", "CODE-TWO" };

        await using var context = _fixture.CreateContext();
        var order = Order.Create(
            null,
            buyerEmail,
            [new OrderLine(Guid.NewGuid(), "Product", "Standard", 1_000m, codes.Length)],
            now);
        order.MarkPaid($"pay-{Guid.NewGuid():N}", now);
        order.MarkAwaitingFulfillment(now);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var item = order.Items.Single();
        foreach (var code in codes)
        {
            var (blob, version) = cipher.Encrypt(code);
            var key = new Key(item.ProductVariantId, blob, version, "seed-admin", now);
            context.Keys.Add(key); // client-generated Id — must be added explicitly (see AttachKeyToOrderItem)
            order.AttachKey(item.Id, key, now);
        }

        await context.SaveChangesAsync();
        Assert.Equal(OrderStatus.Delivered, order.Status);

        return (order.Id, buyerEmail, codes);
    }
}
