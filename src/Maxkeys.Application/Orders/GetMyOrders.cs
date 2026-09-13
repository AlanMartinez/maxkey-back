using Maxkeys.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Orders;

/// <summary>Lists orders owned by the caller (orders-history spec: My Orders Listing).</summary>
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
            .Where(order => order.UserId == userId)
            .OrderByDescending(order => order.CreatedAt)
            .Select(order => new OrderSummary(order.Id, order.Status, order.TotalAmount, order.Currency, order.CreatedAt, order.Items.Count))
            .ToListAsync(cancellationToken);
    }
}
