using Maxkeys.Application.Persistence;
using Maxkeys.Application.Security;
using Maxkeys.Domain.Common;
using Maxkeys.Domain.Keys;
using Maxkeys.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Maxkeys.Application.Orders;

/// <summary>
/// Buyer action that reveals every key assigned to one order item, once
/// (admin-key-delivery-gate spec: decision 2/4, "revelar key"). Same
/// ownership-hiding pattern as <see cref="GetMyOrder"/>: returns
/// <see langword="null"/> both for an unknown order and one owned by someone
/// else (ADR-14). Only allowed once the order is <see cref="OrderStatus.Delivered"/>.
/// Idempotent: a key already <see cref="KeyStatus.Revealed"/> is returned
/// again without re-mutating, so a buyer can safely revisit and still see the
/// code.
/// </summary>
public sealed class RevealOrderItemKeys
{
    private readonly IAppDbContext _db;
    private readonly KeyCipher _keyCipher;
    private readonly ILogger<RevealOrderItemKeys> _logger;

    public RevealOrderItemKeys(IAppDbContext db, KeyCipher keyCipher, ILogger<RevealOrderItemKeys> logger)
    {
        _db = db;
        _keyCipher = keyCipher;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>?> ExecuteAsync(Guid orderId, Guid itemId, Guid userId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order is null || order.UserId != userId)
        {
            return null;
        }

        try
        {
            return await RevealAsync(order, itemId, userId, orderId, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            ClearTracker();

            // Reveal is idempotent — a concurrent reveal of the same item (e.g. a double-clicked
            // button) may have already flipped these keys to Revealed between our read and write;
            // reloading and retrying once returns the same codes either way, matching
            // AttachKeyToOrderItem's concurrency-retry pattern.
            order = await LoadOrderAsync(orderId, cancellationToken)
                ?? throw new DomainConflictException("Order no longer exists.");

            return await RevealAsync(order, itemId, userId, orderId, cancellationToken);
        }
    }

    private async Task<IReadOnlyList<string>> RevealAsync(
        Order order, Guid itemId, Guid userId, Guid orderId, CancellationToken cancellationToken)
    {
        if (order.Status != OrderStatus.Delivered)
        {
            throw new DomainConflictException("Cannot reveal keys unless the order is Delivered.");
        }

        var item = order.Items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new DomainException("Order item does not belong to this order.");

        var now = DateTimeOffset.UtcNow;
        var keys = item.Keys
            .Where(k => k.Status is KeyStatus.Assigned or KeyStatus.Revealed)
            .OrderBy(k => k.AssignedAt)
            .ToList();

        var revealedCount = 0;
        foreach (var key in keys.Where(k => k.Status == KeyStatus.Assigned))
        {
            key.Reveal(now, userId.ToString());
            revealedCount++;
        }

        if (revealedCount > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Buyer {UserId} revealed {Count} key(s) for order {OrderId} item {ItemId}.", userId, revealedCount, orderId, itemId);
        }

        return keys.Select(k => _keyCipher.Decrypt(k.EncryptedCode, k.KeyVersion)).ToList();
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
}
