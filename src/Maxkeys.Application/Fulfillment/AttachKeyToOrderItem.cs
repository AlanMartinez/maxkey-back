using Maxkeys.Application.Persistence;
using Maxkeys.Application.Security;
using Maxkeys.Domain.Common;
using Maxkeys.Domain.Keys;
using Maxkeys.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Fulfillment;

/// <summary>
/// Attaches exactly one key code to an order item (fulfillment spec: Key
/// Attachment, All-or-Nothing Delivery Derivation, Key Encryption at Rest;
/// design section 6d; admin-key-delivery-gate spec: decision 1 — MODIFIED).
/// Encrypts the plaintext code immediately, then persists the key and the
/// order's derived status in a single <c>SaveChangesAsync</c>. Completing
/// every item stops at <see cref="OrderStatus.KeysAssigned"/> — an admin must
/// separately call <c>DeliverOrder</c> to release the order to its buyer, so
/// no outbox event is inserted here any more. Returns <see langword="null"/>
/// for an unknown order so the API layer can map it to 404, matching
/// <c>GetOrderStatus</c>/<c>GetProductBySlug</c>.
/// </summary>
public sealed class AttachKeyToOrderItem
{
    private readonly IAppDbContext _db;
    private readonly KeyCipher _keyCipher;

    public AttachKeyToOrderItem(IAppDbContext db, KeyCipher keyCipher)
    {
        _db = db;
        _keyCipher = keyCipher;
    }

    public async Task<AttachKeyToOrderItemResult?> ExecuteAsync(
        Guid orderId, Guid orderItemId, string code, string loadedBy, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new DomainException("Key code must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(loadedBy))
        {
            throw new DomainException("Loaded-by identity must not be empty.");
        }

        var (blob, version) = _keyCipher.Encrypt(code);
        var now = DateTimeOffset.UtcNow;

        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        try
        {
            return await AttachAsync(order, orderItemId, blob, version, loadedBy, now, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            ClearTracker();

            // Reloaded state is authoritative: if the item is now full, AttachKey itself throws
            // DomainConflictException, so the losing side of a concurrent attach sees 409 either way
            // (design section 5: "AttachKeyToOrderItem ... catch DbUpdateConcurrencyException, reload,
            // re-run the domain call once, then rethrow as DomainConflictException if it conflicts again").
            var reloaded = await LoadOrderAsync(orderId, cancellationToken)
                ?? throw new DomainConflictException("Order no longer exists.");

            try
            {
                return await AttachAsync(reloaded, orderItemId, blob, version, loadedBy, now, cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new DomainConflictException("Order item was modified concurrently; please retry.");
            }
        }
    }

    private async Task<AttachKeyToOrderItemResult> AttachAsync(
        Order order, Guid orderItemId, byte[] blob, short version, string loadedBy, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var item = order.Items.FirstOrDefault(i => i.Id == orderItemId)
            ?? throw new DomainException("Order item does not belong to this order.");

        var key = new Key(item.ProductVariantId, blob, version, loadedBy, now);

        // Key.Id is client-generated (Entity base class), so EF Core's navigation-fixup change
        // detection cannot tell this is a brand-new row once it is only reachable through the
        // already-tracked item's Keys collection — it must be added to the DbSet explicitly, the
        // same way OutboxEvent rows are (ProcessPaymentNotification), or EF issues an UPDATE
        // instead of an INSERT and a spurious DbUpdateConcurrencyException follows.
        _db.Keys.Add(key);
        order.AttachKey(orderItemId, key, now);

        await _db.SaveChangesAsync(cancellationToken);

        return new AttachKeyToOrderItemResult(
            order.Status,
            order.Items
                .Select(i => new AttachKeyToOrderItemResultItem(i.Id, i.Quantity, i.Keys.Count(k => k.Status == KeyStatus.Assigned)))
                .ToList());
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
