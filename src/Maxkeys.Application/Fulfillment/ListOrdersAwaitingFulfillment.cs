using Maxkeys.Application.Persistence;
using Maxkeys.Domain.Keys;
using Maxkeys.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Fulfillment;

/// <summary>
/// Lists orders <see cref="OrderStatus.AwaitingFulfillment"/> with per-item
/// fulfillment progress for the admin queue (fulfillment spec: Admin Order
/// Listing). Restricted to admins at the API layer (PR10 admin policy).
/// </summary>
public sealed class ListOrdersAwaitingFulfillment
{
    private readonly IAppDbContext _db;

    public ListOrdersAwaitingFulfillment(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<AdminOrderSummary>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var orders = await _db.Orders
            .Include(o => o.Items).ThenInclude(i => i.Keys)
            .Where(o => o.Status == OrderStatus.AwaitingFulfillment)
            .OrderBy(o => o.PaidAt)
            .ToListAsync(cancellationToken);

        return orders.Select(ToSummary).ToList();
    }

    private static AdminOrderSummary ToSummary(Order order) => new(
        order.Id,
        order.BuyerEmail,
        order.Status,
        order.PaidAt,
        order.Items
            .Select(item => new AdminOrderItemSummary(
                item.Id,
                item.ProductVariantId,
                item.ProductNameSnapshot,
                item.VariantNameSnapshot,
                item.Quantity,
                item.Keys.Count(k => k.Status == KeyStatus.Assigned)))
            .ToList());
}
