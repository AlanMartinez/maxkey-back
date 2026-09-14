using Maxkeys.Application.Outbox;
using Maxkeys.Application.Security;
using Maxkeys.Application.Tests.Fakes;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Orders;
using Maxkeys.Domain.Outbox;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Tests.Outbox;

/// <summary>
/// Covers <see cref="OrderDeliveryResendHandler"/> (admin-buyers spec: Resend
/// Delivery Email; design D1): sends the same delivery email content as the
/// original send, built via the shared <see cref="DeliveryEmailItems"/>.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OrderDeliveryResendHandlerTests
{
    private static readonly string ValidKeyBase64 =
        Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private readonly PostgresFixture _fixture;

    public OrderDeliveryResendHandlerTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Resend_sends_delivery_email_with_the_same_keys_via_recording_sender()
    {
        var (orderId, buyerEmail, codes) = await SeedDeliveredOrderAsync();
        var emailSender = new RecordingEmailSender();

        await using var context = _fixture.CreateContext();
        var handler = CreateHandler(context, emailSender);

        await handler.HandleAsync(ResendRequestedEvent(orderId), CancellationToken.None);

        var email = Assert.Single(emailSender.SentMessages);
        Assert.Equal(buyerEmail, email.To);
        foreach (var code in codes)
        {
            Assert.Contains(code, email.TextBody);
        }
    }

    private static OrderDeliveryResendHandler CreateHandler(AppDbContext context, RecordingEmailSender emailSender) =>
        new(context, emailSender, new KeyCipher(Options.Create(new KeyCipherOptions { EncryptionKey = ValidKeyBase64, CurrentVersion = 1 })));

    private static OutboxEvent ResendRequestedEvent(Guid orderId) =>
        new(OutboxEventTypes.OrderDeliveryResendRequested,
            $$"""{"orderId":"{{orderId}}","requestedBy":"admin-sub","requestedAt":"{{DateTimeOffset.UtcNow:O}}"}""",
            DateTimeOffset.UtcNow);

    private async Task<(Guid OrderId, string BuyerEmail, IReadOnlyList<string> Codes)> SeedDeliveredOrderAsync()
    {
        var cipher = new KeyCipher(Options.Create(new KeyCipherOptions { EncryptionKey = ValidKeyBase64, CurrentVersion = 1 }));
        var now = DateTimeOffset.UtcNow;
        var buyerEmail = $"buyer-{Guid.NewGuid():N}@example.com";
        var codes = new[] { "CODE-ONE", "CODE-TWO" };

        await using var context = _fixture.CreateContext();
        var order = Order.Create(
            null, buyerEmail, [new OrderLine(Guid.NewGuid(), "Product", "Standard", 1_000m, codes.Length)], now);
        order.MarkPaid($"pay-{Guid.NewGuid():N}", now);
        order.MarkAwaitingFulfillment(now);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var item = order.Items.Single();
        foreach (var code in codes)
        {
            var (blob, version) = cipher.Encrypt(code);
            var key = new Maxkeys.Domain.Keys.Key(item.ProductVariantId, blob, version, "seed-admin", now);
            context.Keys.Add(key); // client-generated Id — must be added explicitly (see AttachKeyToOrderItem)
            order.AttachKey(item.Id, key, now);
        }

        await context.SaveChangesAsync();
        Assert.Equal(OrderStatus.Delivered, order.Status);

        return (order.Id, buyerEmail, codes);
    }
}
