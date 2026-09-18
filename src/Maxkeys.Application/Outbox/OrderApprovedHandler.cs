using System.Text.Json;
using Maxkeys.Application.Fulfillment;
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
/// spec: OrderApproved Handler; design section 6c; admin-key-delivery-gate
/// spec: decision 3 — MODIFIED). Transitions the order
/// <see cref="OrderStatus.Paid"/> → <see cref="OrderStatus.AwaitingFulfillment"/>,
/// then delegates vault auto-assignment to <see cref="AssignVaultKeysToOrder"/>
/// (shared with the admin "Asignar" action). Always emails the operator
/// afterwards — this handler never reaches <see cref="OrderStatus.Delivered"/>
/// on its own any more, an admin always reviews before delivery. Idempotent:
/// an order already past <see cref="OrderStatus.Paid"/> is a logged no-op with
/// no email; an order that does not exist is a bug in the producer, so this
/// throws to let the outbox processor retry/dead-letter rather than silently
/// drop the event.
/// </summary>
public sealed class OrderApprovedHandler : IOutboxHandler
{
    private readonly IAppDbContext _db;
    private readonly AssignVaultKeysToOrder _assignVaultKeysToOrder;
    private readonly IEmailSender _emailSender;
    private readonly IOptions<EmailOptions> _emailOptions;
    private readonly ILogger<OrderApprovedHandler> _logger;

    public string EventType => OutboxEventTypes.OrderApproved;

    public OrderApprovedHandler(
        IAppDbContext db,
        AssignVaultKeysToOrder assignVaultKeysToOrder,
        IEmailSender emailSender,
        IOptions<EmailOptions> emailOptions,
        ILogger<OrderApprovedHandler> logger)
    {
        _db = db;
        _assignVaultKeysToOrder = assignVaultKeysToOrder;
        _emailSender = emailSender;
        _emailOptions = emailOptions;
        _logger = logger;
    }

    public async Task HandleAsync(OutboxEvent evt, CancellationToken cancellationToken)
    {
        var orderId = ParseOrderId(evt.Payload);
        var now = DateTimeOffset.UtcNow;

        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order is null)
        {
            throw new InvalidOperationException($"OrderApproved event references unknown order {orderId}.");
        }

        if (IsFullyResolved(order.Status))
        {
            _logger.LogInformation(
                "Order {OrderId} is already {Status}; OrderApproved handling is a no-op.", orderId, order.Status);
            return;
        }

        if (order.Status == OrderStatus.Paid)
        {
            try
            {
                order.MarkAwaitingFulfillment(now);
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                DetachHandlerState();

                order = await LoadOrderAsync(orderId, cancellationToken)
                    ?? throw new InvalidOperationException($"OrderApproved event references unknown order {orderId}.");

                if (IsFullyResolved(order.Status))
                {
                    _logger.LogInformation(
                        "Order {OrderId} is already {Status} after a concurrent update; OrderApproved handling is a no-op.",
                        orderId, order.Status);
                    return;
                }

                if (order.Status == OrderStatus.Paid)
                {
                    order.MarkAwaitingFulfillment(now);
                    await _db.SaveChangesAsync(cancellationToken);
                }
            }
        }

        // order.Status is now AwaitingFulfillment — either just transitioned above, or already was on a
        // resumed retry after a prior attempt's assign step failed past this point. Re-attempting the
        // assign + operator email here is safe: AssignVaultKeysToOrder is idempotent (it only touches
        // items that are still incomplete), and retrying here is what makes a failed assign attempt
        // recoverable via the outbox processor's normal retry/backoff instead of silently stranding the
        // order (whole-branch review finding, Important #2).
        var assignResult = await _assignVaultKeysToOrder.ExecuteAsync(orderId, cancellationToken);
        _logger.LogInformation(
            "Vault auto-assign for order {OrderId}: all items complete = {AllComplete}.",
            orderId, assignResult?.AllItemsComplete ?? false);

        // `order` may be a stale (detached) reference if AssignVaultKeysToOrder's own concurrency retry
        // ran against the same shared DbContext — safe here regardless, since the operator email only
        // reads already-materialized scalar fields and the item collection, never lazy-loaded state.
        var (subject, textBody) = EmailTemplates.OperatorOrderAwaitingFulfillment(order);
        await _emailSender.SendAsync(new EmailMessage(_emailOptions.Value.OperatorTo, subject, textBody), cancellationToken);
    }

    private static bool IsFullyResolved(OrderStatus status) =>
        status is OrderStatus.KeysAssigned or OrderStatus.Delivered;

    private Task<Order?> LoadOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
        _db.Orders
            .Include(o => o.Items).ThenInclude(i => i.Keys)
            .SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);

    /// <summary>
    /// Detaches only what THIS handler's failed attempt added to the change
    /// tracker, on a <see cref="DbUpdateConcurrencyException"/> retry. Unlike
    /// <c>AttachKeyToOrderItem</c>'s <c>ClearTracker</c> (safe there because it
    /// runs in a single-purpose per-HTTP-request scope), this handler runs
    /// inside <c>OutboxProcessor.ProcessOnceAsync</c>'s shared batch
    /// <see cref="DbContext"/> (design section 6; <c>OutboxProcessor.cs</c>),
    /// where other claimed <see cref="OutboxEvent"/> rows are also tracked and
    /// still need their <c>MarkProcessed</c>/<c>MarkFailedAttempt</c> mutations
    /// to persist later in the same pass. A full <c>ChangeTracker.Clear()</c>
    /// would detach those too, silently dropping their status updates.
    /// </summary>
    private void DetachHandlerState()
    {
        if (_db is not DbContext dbContext)
        {
            return;
        }

        foreach (var entry in dbContext.ChangeTracker.Entries().ToList())
        {
            if (entry.Entity is OutboxEvent && entry.State != EntityState.Added)
            {
                continue;
            }

            entry.State = EntityState.Detached;
        }
    }

    private static Guid ParseOrderId(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var orderIdText = document.RootElement.GetProperty("orderId").GetString();
        return Guid.Parse(orderIdText!);
    }
}
