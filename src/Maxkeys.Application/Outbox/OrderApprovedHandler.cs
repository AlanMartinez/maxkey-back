using System.Text.Json;
using Maxkeys.Application.Notifications;
using Maxkeys.Application.Persistence;
using Maxkeys.Domain.Orders;
using Maxkeys.Domain.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Outbox;

/// <summary>
/// Handles <see cref="OutboxEventTypes.OrderApproved"/> (outbox-processing
/// spec: OrderApproved Handler; design section 6c). Transitions the order
/// <see cref="OrderStatus.Paid"/> → <see cref="OrderStatus.AwaitingFulfillment"/>
/// and sends the operator notification. Idempotent: an order already past
/// <see cref="OrderStatus.Paid"/> is a logged no-op with no email; an order
/// that does not exist is a bug in the producer, so this throws to let the
/// outbox processor retry/dead-letter rather than silently drop the event.
/// </summary>
public sealed class OrderApprovedHandler : IOutboxHandler
{
    private readonly IAppDbContext _db;
    private readonly IEmailSender _emailSender;
    private readonly IOptions<EmailOptions> _emailOptions;
    private readonly ILogger<OrderApprovedHandler> _logger;

    public string EventType => OutboxEventTypes.OrderApproved;

    public OrderApprovedHandler(
        IAppDbContext db,
        IEmailSender emailSender,
        IOptions<EmailOptions> emailOptions,
        ILogger<OrderApprovedHandler> logger)
    {
        _db = db;
        _emailSender = emailSender;
        _emailOptions = emailOptions;
        _logger = logger;
    }

    public async Task HandleAsync(OutboxEvent evt, CancellationToken cancellationToken)
    {
        var orderId = ParseOrderId(evt.Payload);

        var order = await _db.Orders
            .Include(o => o.Items)
            .SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        if (order is null)
        {
            throw new InvalidOperationException($"OrderApproved event references unknown order {orderId}.");
        }

        if (order.Status is OrderStatus.AwaitingFulfillment or OrderStatus.Delivered)
        {
            _logger.LogInformation(
                "Order {OrderId} is already {Status}; OrderApproved handling is a no-op.", orderId, order.Status);
            return;
        }

        order.MarkAwaitingFulfillment(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);

        var (subject, textBody) = EmailTemplates.OperatorOrderAwaitingFulfillment(order);
        await _emailSender.SendAsync(new EmailMessage(_emailOptions.Value.OperatorTo, subject, textBody), cancellationToken);
    }

    private static Guid ParseOrderId(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var orderIdText = document.RootElement.GetProperty("orderId").GetString();
        return Guid.Parse(orderIdText!);
    }
}
