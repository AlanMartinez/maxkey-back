using System.Text.Json;
using Maxkeys.Application.Notifications;
using Maxkeys.Application.Persistence;
using Maxkeys.Application.Security;
using Maxkeys.Domain.Orders;
using Maxkeys.Domain.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Maxkeys.Application.Outbox;

/// <summary>
/// Handles <see cref="OutboxEventTypes.OrderDelivered"/> (outbox-processing
/// spec; design section 6d; ADR-04). Decrypts each assigned key in memory only
/// and sends one buyer email grouped by item. The <see cref="OutboxEvent"/> row
/// is inserted exactly once — <c>AttachKey</c> only fires it on the single
/// <c>AwaitingFulfillment</c> → <c>Delivered</c> transition, which is terminal
/// (design ADR-04) — but the processor may still hand this handler the same
/// already-<see cref="OutboxEventStatus.Processed"/> row back on a defensive
/// re-invocation, so that case is a logged no-op with no second email
/// (fulfillment spec: One-Time Delivery Email). A crash between a successful
/// send and marking the row <see cref="OutboxEventStatus.Processed"/> remains
/// at-least-once by design (ADR-04) and may still duplicate the send.
/// </summary>
public sealed class OrderDeliveredHandler : IOutboxHandler
{
    private readonly IAppDbContext _db;
    private readonly IEmailSender _emailSender;
    private readonly KeyCipher _keyCipher;
    private readonly ILogger<OrderDeliveredHandler> _logger;

    public string EventType => OutboxEventTypes.OrderDelivered;

    public OrderDeliveredHandler(IAppDbContext db, IEmailSender emailSender, KeyCipher keyCipher, ILogger<OrderDeliveredHandler> logger)
    {
        _db = db;
        _emailSender = emailSender;
        _keyCipher = keyCipher;
        _logger = logger;
    }

    public async Task HandleAsync(OutboxEvent evt, CancellationToken cancellationToken)
    {
        if (evt.Status == OutboxEventStatus.Processed)
        {
            _logger.LogInformation("OutboxEvent {EventId} is already processed; OrderDelivered handling is a no-op.", evt.Id);
            return;
        }

        var orderId = ParseOrderId(evt.Payload);

        var order = await _db.Orders
            .Include(o => o.Items).ThenInclude(i => i.Keys)
            .SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        if (order is null)
        {
            throw new InvalidOperationException($"OrderDelivered event references unknown order {orderId}.");
        }

        if (order.Status != OrderStatus.Delivered)
        {
            throw new InvalidOperationException(
                $"OrderDelivered event references order {orderId} that is not Delivered (status: {order.Status}).");
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
