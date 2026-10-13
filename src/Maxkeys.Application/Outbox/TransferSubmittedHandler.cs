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
/// Handles <see cref="OutboxEventTypes.TransferSubmitted"/>: emails the operator
/// (<see cref="EmailOptions.OperatorTo"/>) that a bank-transfer order awaits validation.
/// A no-op once an admin already approved or rejected the order — the email would only
/// prompt work that is done. An unknown order is a producer bug, so this throws to let
/// the outbox processor retry/dead-letter.
/// </summary>
public sealed class TransferSubmittedHandler : IOutboxHandler
{
    private readonly IAppDbContext _db;
    private readonly IEmailSender _emailSender;
    private readonly IOptions<EmailOptions> _emailOptions;
    private readonly ILogger<TransferSubmittedHandler> _logger;

    public string EventType => OutboxEventTypes.TransferSubmitted;

    public TransferSubmittedHandler(
        IAppDbContext db,
        IEmailSender emailSender,
        IOptions<EmailOptions> emailOptions,
        ILogger<TransferSubmittedHandler> logger)
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
            throw new InvalidOperationException($"TransferSubmitted event references unknown order {orderId}.");
        }

        if (order.Status != OrderStatus.AwaitingTransferValidation)
        {
            _logger.LogInformation(
                "Order {OrderId} is already {Status}; TransferSubmitted handling is a no-op.", orderId, order.Status);
            return;
        }

        var (subject, textBody) = EmailTemplates.OperatorTransferAwaitingValidation(order);
        await _emailSender.SendAsync(new EmailMessage(_emailOptions.Value.OperatorTo, subject, textBody), cancellationToken);
    }

    private static Guid ParseOrderId(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var orderIdText = document.RootElement.GetProperty("orderId").GetString();
        return Guid.Parse(orderIdText!);
    }
}
