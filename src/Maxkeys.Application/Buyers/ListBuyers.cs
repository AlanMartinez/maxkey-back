using Maxkeys.Application.Persistence;
using Maxkeys.Domain.Keys;
using Maxkeys.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Buyers;

/// <summary>
/// Lists buyers grouped by <see cref="Order.BuyerEmail"/>, each with their
/// paid orders, items, and assigned-key counts (admin-buyers spec: Buyer
/// Listing Grouped By Email, Key Exposure in Buyer View; design D4). Two
/// queries: Q1 groups paid orders (<c>PaidAt != null</c>) by email, applies
/// the optional case-insensitive substring search (same pattern as
/// <c>GetCatalog</c>), orders by last <c>PaidAt</c> descending, and pages;
/// Q2 loads the full orders/items/keys for only that page's emails. Never
/// returns a key code — only the count of <see cref="KeyStatus.Assigned"/>
/// keys per item.
/// </summary>
public sealed class ListBuyers
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    private readonly IAppDbContext _db;

    public ListBuyers(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<BuyersPage> ExecuteAsync(
        string? email, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

        var paidOrders = _db.Orders.Where(o => o.PaidAt != null);

        if (!string.IsNullOrWhiteSpace(email))
        {
            var pattern = email.ToLower();
            paidOrders = paidOrders.Where(o => o.BuyerEmail.ToLower().Contains(pattern));
        }

        var grouped = paidOrders
            .GroupBy(o => o.BuyerEmail)
            .Select(g => new { Email = g.Key, OrderCount = g.Count(), LastPaidAt = g.Max(o => o.PaidAt) })
            .OrderByDescending(g => g.LastPaidAt);

        var total = await grouped.CountAsync(cancellationToken);

        var page1 = await grouped
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var emails = page1.Select(g => g.Email).ToList();

        var orders = await _db.Orders
            .Include(o => o.Items).ThenInclude(i => i.Keys)
            .Where(o => o.PaidAt != null && emails.Contains(o.BuyerEmail))
            .ToListAsync(cancellationToken);

        var ordersByEmail = orders
            .GroupBy(o => o.BuyerEmail)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(o => o.PaidAt).ToList());

        var items = page1
            .Select(g => new AdminBuyer(
                g.Email,
                g.OrderCount,
                g.LastPaidAt,
                ordersByEmail.TryGetValue(g.Email, out var buyerOrders)
                    ? buyerOrders.Select(ToAdminBuyerOrder).ToList()
                    : []))
            .ToList();

        return new BuyersPage(items, page, pageSize, total);
    }

    private static AdminBuyerOrder ToAdminBuyerOrder(Order order) => new(
        order.Id,
        order.Status.ToString(),
        order.PaidAt,
        order.TotalAmount,
        order.Currency,
        order.Items
            .Select(item => new AdminBuyerOrderItem(
                item.ProductNameSnapshot,
                item.VariantNameSnapshot,
                item.Quantity,
                item.Keys.Count(k => k.Status == KeyStatus.Assigned)))
            .ToList());
}
