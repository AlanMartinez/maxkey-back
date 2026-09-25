using Maxkeys.Application.Persistence;
using Maxkeys.Application.Security;
using Maxkeys.Domain.Keys;
using Maxkeys.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Orders;

/// <summary>
/// Resolves one order's detail scoped to its owner (orders-history spec: Order
/// Detail With Conditional Key Reveal, Ownership Enforcement; admin-key-delivery-gate
/// spec: decision 2/4 — MODIFIED). Returns <see langword="null"/> both for an
/// unknown order and for an order owned by someone else — the API layer maps
/// either case to 404, never revealing that the order exists (ADR-14; matches
/// <c>GetOrderStatus</c>/<c>GetProductBySlug</c>).
/// </summary>
public sealed class GetMyOrder
{
    private readonly IAppDbContext _db;
    private readonly KeyCipher _keyCipher;

    public GetMyOrder(IAppDbContext db, KeyCipher keyCipher)
    {
        _db = db;
        _keyCipher = keyCipher;
    }

    public async Task<MyOrderDetail?> ExecuteAsync(Guid orderId, Guid userId, CancellationToken cancellationToken = default)
    {
        var order = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Items).ThenInclude(i => i.Keys)
            .SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        if (order is null || order.UserId != userId)
        {
            return null;
        }

        var items = order.Items
            .Select(item => new MyOrderItemDetail(
                item.Id,
                item.ProductNameSnapshot,
                item.VariantNameSnapshot,
                item.UnitPrice,
                item.Quantity,
                item.Keys
                    .Where(key => key.Status == KeyStatus.Revealed)
                    .Select(key => _keyCipher.Decrypt(key.EncryptedCode, key.KeyVersion))
                    .ToList(),
                order.Status == OrderStatus.Delivered && item.Keys.Any(key => key.Status == KeyStatus.Assigned)))
            .ToList();

        return new MyOrderDetail(order.Id, order.Status.ToString(), order.TotalAmount, order.Currency, order.CreatedAt, items);
    }
}
