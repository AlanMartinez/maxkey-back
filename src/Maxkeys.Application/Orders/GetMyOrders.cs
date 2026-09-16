using Maxkeys.Application.Persistence;
using Maxkeys.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Orders;

/// <summary>
/// Lists orders owned by the caller (orders-history spec: My Orders Listing).
/// Excludes <see cref="OrderStatus.Pending"/> and <see cref="OrderStatus.Cancelled"/>
/// orders: an order only belongs in "my purchases" once Mercado Pago has
/// confirmed payment, not while checkout is still in flight.
/// </summary>
public sealed class GetMyOrders
{
    private readonly IAppDbContext _db;

    public GetMyOrders(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<OrderSummary>> ExecuteAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _db.Orders
            .Where(order => order.UserId == userId
                && order.Status != OrderStatus.Pending
                && order.Status != OrderStatus.Cancelled)
            .OrderByDescending(order => order.CreatedAt)
            .Select(order => new OrderSummary(order.Id, order.Status, order.TotalAmount, order.Currency, order.CreatedAt, order.Items.Count))
            .ToListAsync(cancellationToken);
    }
}
