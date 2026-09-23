using System.Text.Json;
using Maxkeys.Application.Notifications;
using Maxkeys.Application.Persistence;
using Maxkeys.Domain.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Outbox;

/// <summary>
/// Handles <see cref="OutboxEventTypes.OrderDelivered"/> (admin-key-delivery-gate
/// spec: decision 4 revisited — <c>DeliverOrder</c> now raises this instead of
/// staying silent). Sends <see cref="EmailTemplates.BuyerOrderReady"/>, which
/// never contains key codes — only a link to <see cref="EmailOptions.AccountOrdersUrl"/>,
/// where the buyer reveals each key themselves via <c>RevealOrderItemKeys</c>.
/// </summary>
public sealed class OrderDeliveredHandler : IOutboxHandler
{
    private readonly IAppDbContext _db;
    private readonly IEmailSender _emailSender;
    private readonly IOptions<EmailOptions> _emailOptions;

    public string EventType => OutboxEventTypes.OrderDelivered;

    public OrderDeliveredHandler(IAppDbContext db, IEmailSender emailSender, IOptions<EmailOptions> emailOptions)
    {
        _db = db;
        _emailSender = emailSender;
        _emailOptions = emailOptions;
    }

    public async Task HandleAsync(OutboxEvent evt, CancellationToken cancellationToken)
    {
        var orderId = ParseOrderId(evt.Payload);

        var order = await _db.Orders.SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        if (order is null)
        {
            throw new InvalidOperationException($"OrderDelivered event references unknown order {orderId}.");
        }

        var (subject, textBody, htmlBody) = EmailTemplates.BuyerOrderReady(order, _emailOptions.Value.AccountOrdersUrl);
        await _emailSender.SendAsync(new EmailMessage(order.BuyerEmail, subject, textBody, htmlBody), cancellationToken);
    }

    private static Guid ParseOrderId(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var orderIdText = document.RootElement.GetProperty("orderId").GetString();
        return Guid.Parse(orderIdText!);
    }
}
