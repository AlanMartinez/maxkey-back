using Maxkeys.Application.Outbox;
using Maxkeys.Application.Persistence;
using Maxkeys.Domain.Keys;
using Maxkeys.Domain.Orders;
using Maxkeys.Domain.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Buyers;

/// <summary>
/// Loads one order with its items, attached keys, and the outbox events that
/// reference it, for the admin buyers detail modal (admin-buyers spec: Order
/// Detail, Key Exposure in Buyer View). Never returns a key code — only each
/// key's id, status, assignment/reveal timestamps, and who revealed it.
/// Returns <c>null</c> when the order does not exist.
/// </summary>
public sealed class GetAdminOrderDetail
{
    private readonly IAppDbContext _db;
    private readonly IOutboxEventReader _outboxEvents;

    public GetAdminOrderDetail(IAppDbContext db, IOutboxEventReader outboxEvents)
    {
        _db = db;
        _outboxEvents = outboxEvents;
    }

    public async Task<AdminOrderDetail?> ExecuteAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Items).ThenInclude(i => i.Keys)
            .SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        if (order is null)
        {
            return null;
        }

        var events = await _outboxEvents.ListByOrderIdAsync(orderId, cancellationToken);

        return new AdminOrderDetail(
            order.Id,
            order.BuyerEmail,
            order.Status.ToString(),
            order.TotalAmount,
            order.Currency,
            order.CreatedAt,
            order.PaidAt,
            order.DeliveredAt,
            order.UpdatedAt,
            order.MpPaymentId,
            order.LastPaymentAttemptStatus,
            order.LastPaymentAttemptAt,
            order.Items.Select(ToItem).ToList(),
            events.Select(ToEvent).ToList());
    }

    private static AdminOrderDetailItem ToItem(OrderItem item) => new(
        item.Id,
        item.ProductNameSnapshot,
        item.VariantNameSnapshot,
        item.UnitPrice,
        item.Quantity,
        item.Keys.Select(ToKey).ToList());

    private static AdminOrderDetailKey ToKey(Key key) =>
        new(key.Id, key.Status.ToString(), key.AssignedAt, key.RevealedAt, key.RevealedBy);

    private static AdminOrderDetailEvent ToEvent(OutboxEvent evt) => new(
        evt.Id,
        evt.Type,
        evt.Status.ToString(),
        evt.CreatedAt,
        evt.ProcessedAt,
        evt.Attempts,
        evt.LastError);
}
