using System.Text.Json;
using Maxkeys.Application.Notifications;
using Maxkeys.Application.Persistence;
using Maxkeys.Domain.Keys;
using Maxkeys.Domain.Orders;
using Maxkeys.Domain.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Outbox;

/// <summary>
/// Handles <see cref="OutboxEventTypes.OrderApproved"/> (outbox-processing
/// spec: OrderApproved Handler; design section 6c; vault spec: Vault
/// Auto-Assignment On Approval). Transitions the order
/// <see cref="OrderStatus.Paid"/> → <see cref="OrderStatus.AwaitingFulfillment"/>,
/// then attempts to auto-assign vault stock to every item whose product has
/// vault auto-fulfillment enabled (all-or-nothing per item — see
/// <see cref="AutoAssignVaultKeysAsync"/>). Sends the operator notification
/// only if the order is not fully <see cref="OrderStatus.Delivered"/>
/// afterwards. Idempotent: an order already past <see cref="OrderStatus.Paid"/>
/// is a logged no-op with no email; an order that does not exist is a bug in
/// the producer, so this throws to let the outbox processor retry/dead-letter
/// rather than silently drop the event.
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
        var now = DateTimeOffset.UtcNow;

        var order = await LoadOrderAsync(orderId, cancellationToken);
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

        try
        {
            await ApproveAsync(order, now, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            ClearTracker();

            // Same reload-and-retry-once pattern as AttachKeyToOrderItem: a concurrent auto-assign
            // for another order may have consumed the same vault keys between our read and write.
            order = await LoadOrderAsync(orderId, cancellationToken)
                ?? throw new InvalidOperationException($"OrderApproved event references unknown order {orderId}.");

            if (order.Status is OrderStatus.AwaitingFulfillment or OrderStatus.Delivered)
            {
                _logger.LogInformation(
                    "Order {OrderId} is already {Status} after a concurrent update; OrderApproved handling is a no-op.",
                    orderId, order.Status);
                return;
            }

            await ApproveAsync(order, now, cancellationToken);
        }

        if (order.Status != OrderStatus.Delivered)
        {
            var (subject, textBody) = EmailTemplates.OperatorOrderAwaitingFulfillment(order);
            await _emailSender.SendAsync(new EmailMessage(_emailOptions.Value.OperatorTo, subject, textBody), cancellationToken);
        }
    }

    private async Task ApproveAsync(Order order, DateTimeOffset now, CancellationToken cancellationToken)
    {
        order.MarkAwaitingFulfillment(now);
        await AutoAssignVaultKeysAsync(order, now, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Attempts to auto-assign vault stock to every incomplete item whose
    /// product has <see cref="Domain.Catalog.Product.VaultEnabled"/> set
    /// (vault spec: Vault Auto-Assignment On Approval). Per item, all-or-nothing:
    /// if available stock is less than the item's remaining quantity, that item
    /// is skipped entirely and left for the existing manual flow. Reuses
    /// <see cref="Order.AttachKey"/> unchanged, so the same completeness/status
    /// derivation applies. Inserts the <see cref="OutboxEventTypes.OrderDelivered"/>
    /// row itself if this pass completes the order — <see cref="Order.AttachKey"/>
    /// only returns whether it did, it does not touch the outbox.
    /// </summary>
    private async Task AutoAssignVaultKeysAsync(Order order, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var incompleteItems = order.Items.Where(i => !i.IsComplete).ToList();
        if (incompleteItems.Count == 0)
        {
            return;
        }

        var variantIds = incompleteItems.Select(i => i.ProductVariantId).Distinct().ToList();
        var variantProductIds = await _db.ProductVariants
            .Where(v => variantIds.Contains(v.Id))
            .Select(v => new { v.Id, v.ProductId })
            .ToListAsync(cancellationToken);
        var productIdByVariant = variantProductIds.ToDictionary(v => v.Id, v => v.ProductId);

        var productIds = productIdByVariant.Values.Distinct().ToList();
        var vaultEnabledByProduct = await _db.Products
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.VaultEnabled })
            .ToDictionaryAsync(p => p.Id, p => p.VaultEnabled, cancellationToken);

        foreach (var item in incompleteItems)
        {
            if (!productIdByVariant.TryGetValue(item.ProductVariantId, out var productId) ||
                !vaultEnabledByProduct.TryGetValue(productId, out var vaultEnabled) ||
                !vaultEnabled)
            {
                continue;
            }

            var remaining = item.Quantity - item.Keys.Count(k => k.Status == KeyStatus.Assigned);
            if (remaining <= 0)
            {
                continue;
            }

            var availableKeys = await _db.Keys
                .Where(k => k.ProductVariantId == item.ProductVariantId && k.Status == KeyStatus.Available)
                .OrderBy(k => k.CreatedAt)
                .Take(remaining)
                .ToListAsync(cancellationToken);

            if (availableKeys.Count < remaining)
            {
                continue; // all-or-nothing: leave this item for the manual flow
            }

            foreach (var key in availableKeys)
            {
                order.AttachKey(item.Id, key, now);
            }
        }

        if (order.Status == OrderStatus.Delivered)
        {
            _db.OutboxEvents.Add(new OutboxEvent(OutboxEventTypes.OrderDelivered, $$"""{"orderId":"{{order.Id}}"}""", now));
        }
    }

    private Task<Order?> LoadOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
        _db.Orders
            .Include(o => o.Items).ThenInclude(i => i.Keys)
            .SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);

    private void ClearTracker()
    {
        if (_db is DbContext dbContext)
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    private static Guid ParseOrderId(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var orderIdText = document.RootElement.GetProperty("orderId").GetString();
        return Guid.Parse(orderIdText!);
    }
}
