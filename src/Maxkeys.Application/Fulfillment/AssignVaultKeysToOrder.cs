using Maxkeys.Application.Persistence;
using Maxkeys.Domain.Common;
using Maxkeys.Domain.Keys;
using Maxkeys.Domain.Orders;
using Maxkeys.Domain.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Fulfillment;

/// <summary>
/// Attempts to auto-assign vault stock to every incomplete item of one order
/// whose product has vault auto-fulfillment enabled (admin-key-delivery-gate
/// spec: decision 3; vault spec: Vault Auto-Assignment On Approval). Per item,
/// all-or-nothing: if available stock is less than the item's remaining
/// quantity, that item is skipped entirely. Called both automatically by
/// <see cref="Outbox.OrderApprovedHandler"/> right after payment approval and
/// on demand by the admin "Asignar" action for orders that didn't have full
/// stock at approval time. A no-op (current state returned unchanged) for any
/// order not <see cref="OrderStatus.AwaitingFulfillment"/> — nothing left to
/// assign. Returns <see langword="null"/> for an unknown order id.
/// </summary>
public sealed class AssignVaultKeysToOrder
{
    private readonly IAppDbContext _db;

    public AssignVaultKeysToOrder(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<AssignVaultKeysToOrderResult?> ExecuteAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        if (order.Status != OrderStatus.AwaitingFulfillment)
        {
            return ToResult(order);
        }

        try
        {
            await AssignAsync(order, now, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            DetachTrackedState();

            // Same reload-and-retry-once pattern as AttachKeyToOrderItem: a concurrent auto-assign
            // for another order may have consumed the same vault keys between our read and write.
            order = await LoadOrderAsync(orderId, cancellationToken)
                ?? throw new DomainConflictException("Order no longer exists.");

            if (order.Status != OrderStatus.AwaitingFulfillment)
            {
                return ToResult(order);
            }

            await AssignAsync(order, now, cancellationToken);
        }

        return ToResult(order);
    }

    private async Task AssignAsync(Order order, DateTimeOffset now, CancellationToken cancellationToken)
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

        await _db.SaveChangesAsync(cancellationToken);
    }

    private Task<Order?> LoadOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
        _db.Orders
            .Include(o => o.Items).ThenInclude(i => i.Keys)
            .SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);

    /// <summary>
    /// Detaches only what THIS call's failed attempt added to the change tracker —
    /// safe whether called from a dedicated per-request context (admin "Asignar"
    /// endpoint) or from the outbox processor's shared batch context
    /// (<c>OrderApprovedHandler</c>): a blind <c>ChangeTracker.Clear()</c> would
    /// silently drop other claimed <c>OutboxEvent</c> rows' pending status
    /// mutations in the shared-context case (see
    /// <c>OrderApprovedHandlerTests.Concurrency_retry_does_not_lose_a_sibling_outbox_events_status_update</c>).
    /// </summary>
    private void DetachTrackedState()
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

    private static AssignVaultKeysToOrderResult ToResult(Order order) => new(
        order.Status,
        order.Items.All(i => i.IsComplete),
        order.Items
            .Select(i => new AssignVaultKeysToOrderResultItem(i.Id, i.Quantity, i.Keys.Count(k => k.Status == KeyStatus.Assigned)))
            .ToList());
}

/// <summary>Per-item progress after an assign attempt (admin-key-delivery-gate spec: decision 3).</summary>
public sealed record AssignVaultKeysToOrderResultItem(Guid ItemId, int Quantity, int AssignedKeys);

/// <summary>Order status and per-item progress returned by <see cref="AssignVaultKeysToOrder"/>.</summary>
public sealed record AssignVaultKeysToOrderResult(OrderStatus OrderStatus, bool AllItemsComplete, IReadOnlyList<AssignVaultKeysToOrderResultItem> Items);
