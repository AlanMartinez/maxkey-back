using System.Text.Json;
using Maxkeys.Application.Persistence;
using Maxkeys.Domain.Orders;
using Maxkeys.Domain.Notifications;
using Maxkeys.Domain.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Maxkeys.Application.Fulfillment;

/// <summary>
/// Admin action that releases an order's already-assigned keys to its buyer
/// (admin-key-delivery-gate spec: decision 3, "Entregar"). Valid only from
/// <see cref="OrderStatus.KeysAssigned"/> — <see cref="Order.MarkDelivered"/>
/// throws <see cref="Domain.Common.DomainConflictException"/> (409) otherwise,
/// e.g. an item still lacks stock or the order was already delivered. Returns
/// <see langword="null"/> for an unknown order so the API layer maps it to
/// 404. Delivery opens buyer visibility in <c>/account/orders</c> AND raises
/// <see cref="OutboxEventTypes.OrderDelivered"/> (decision 4 revisited —
/// <c>OrderDeliveredHandler</c> emails the buyer that their keys are ready,
/// without the key codes themselves).
/// </summary>
public sealed class DeliverOrder
{
    private readonly IAppDbContext _db;
    private readonly ILogger<DeliverOrder> _logger;

    public DeliverOrder(IAppDbContext db, ILogger<DeliverOrder> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<OrderStatus?> ExecuteAsync(Guid orderId, string deliveredBy, CancellationToken cancellationToken = default)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        order.MarkDelivered(now);
        if (order.UserId is Guid userId)
        {
            _db.Notifications.Add(new Notification(userId, order.Id, DateTime.UtcNow));
        }

        _db.OutboxEvents.Add(new OutboxEvent(
            OutboxEventTypes.OrderDelivered,
            JsonSerializer.Serialize(new { orderId }),
            now));

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Order {OrderId} marked Delivered by admin {AdminSub}.", orderId, deliveredBy);

        return order.Status;
    }
}
