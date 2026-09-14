using System.Text.Json;
using Maxkeys.Application.Notifications;
using Maxkeys.Application.Persistence;
using Maxkeys.Application.Security;
using Maxkeys.Domain.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Outbox;

/// <summary>
/// Handles <see cref="OutboxEventTypes.OrderDeliveryResendRequested"/>
/// (admin-buyers spec: Resend Delivery Email; design D1) — sends the same
/// delivery email content as <see cref="OrderDeliveredHandler"/>, built from
/// the shared <see cref="DeliveryEmailItems.FromOrder"/>, without altering
/// <c>DeliveredAt</c> or any <c>Key</c> row (fulfillment spec, One-Time
/// Delivery Email — MODIFIED).
/// </summary>
public sealed class OrderDeliveryResendHandler : IOutboxHandler
{
    private readonly IAppDbContext _db;
    private readonly IEmailSender _emailSender;
    private readonly KeyCipher _keyCipher;

    public string EventType => OutboxEventTypes.OrderDeliveryResendRequested;

    public OrderDeliveryResendHandler(IAppDbContext db, IEmailSender emailSender, KeyCipher keyCipher)
    {
        _db = db;
        _emailSender = emailSender;
        _keyCipher = keyCipher;
    }

    public async Task HandleAsync(OutboxEvent evt, CancellationToken cancellationToken)
    {
        var orderId = ParseOrderId(evt.Payload);

        var order = await _db.Orders
            .Include(o => o.Items).ThenInclude(i => i.Keys)
            .SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        if (order is null)
        {
            throw new InvalidOperationException($"OrderDeliveryResendRequested event references unknown order {orderId}.");
        }

        var items = DeliveryEmailItems.FromOrder(order, _keyCipher);
        var (subject, textBody) = EmailTemplates.BuyerOrderDelivered(order, items);
        await _emailSender.SendAsync(new EmailMessage(order.BuyerEmail, subject, textBody), cancellationToken);
    }

    private static Guid ParseOrderId(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var orderIdText = document.RootElement.GetProperty("orderId").GetString();
        return Guid.Parse(orderIdText!);
    }
}
