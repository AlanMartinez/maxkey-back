using Maxkeys.Application.Persistence;
using Maxkeys.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Fulfillment;

/// <summary>
/// Lists bank-transfer orders <see cref="OrderStatus.AwaitingTransferValidation"/>, oldest
/// first, for the admin queue behind <c>GET /admin/orders?status=awaiting-transfer</c>.
/// Restricted to admins at the API layer.
/// </summary>
public sealed class ListOrdersAwaitingTransferValidation
{
    private readonly IAppDbContext _db;

    public ListOrdersAwaitingTransferValidation(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<AdminOrderSummary>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var orders = await _db.Orders
            .Include(o => o.Items).ThenInclude(i => i.Keys)
            .Where(o => o.Status == OrderStatus.AwaitingTransferValidation)
            .OrderBy(o => o.CreatedAt)
            .ToListAsync(cancellationToken);

        return orders.Select(AdminOrderSummaryMapper.ToSummary).ToList();
    }
}
