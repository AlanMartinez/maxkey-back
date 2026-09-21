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

    /// <summary>Claims guest orders by email first so "my orders" reflects pre-login purchases.</summary>
    public async Task<IReadOnlyList<OrderSummary>> ExecuteAsync(Guid userId, string email, CancellationToken cancellationToken = default)
    {
        // Bulk-update (no load-then-save) any guest order left behind by a checkout that
        // matches this login's email, so it becomes owned by this account from now on.
        await _db.Orders
            .Where(order => order.UserId == null && order.BuyerEmail.ToLower() == email.ToLower())
            .ExecuteUpdateAsync(setters => setters.SetProperty(order => order.UserId, (Guid?)userId), cancellationToken);

        // Projects to an anonymous type first (server-translated) and stringifies the enum
        // client-side afterward — HasConversion<string>() only guarantees a clean translation
        // for filtering/ordering by the enum itself, not for calling .ToString() on it inside
        // the SQL-translated Select.
        var orders = await _db.Orders
            .Where(order => order.UserId == userId
                && order.Status != OrderStatus.Pending
                && order.Status != OrderStatus.Cancelled)
            .OrderByDescending(order => order.CreatedAt)
            .Select(order => new { order.Id, order.Status, order.TotalAmount, order.Currency, order.CreatedAt, ItemCount = order.Items.Count })
            .ToListAsync(cancellationToken);

        return orders
            .Select(o => new OrderSummary(o.Id, o.Status.ToString(), o.TotalAmount, o.Currency, o.CreatedAt, o.ItemCount))
            .ToList();
    }
}
