using System.Text.Json;
using Maxkeys.Application.Persistence;
using Maxkeys.Domain.Common;
using Maxkeys.Domain.Orders;
using Maxkeys.Domain.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Fulfillment;

/// <summary>
/// Records an admin's request to resend the delivery email for an order
/// (admin-buyers spec: Resend Delivery Email; fulfillment spec: One-Time
/// Delivery Email — MODIFIED; design D1). Only allowed for orders already
/// <see cref="OrderStatus.Delivered"/>; never touches <c>DeliveredAt</c> or
/// the order's keys — it only inserts an <see cref="OutboxEvent"/> row that
/// <c>OrderDeliveryResendHandler</c> later dispatches. Returns
/// <see langword="null"/> for an unknown order id so the API layer maps it to
/// 404, matching <c>AttachKeyToOrderItem</c>.
/// </summary>
public sealed class RequestDeliveryResend
{
    private readonly IAppDbContext _db;

    public RequestDeliveryResend(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Guid?> ExecuteAsync(Guid orderId, string requestedBy, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(requestedBy))
        {
            throw new DomainException("Requested-by identity must not be empty.");
        }

        var order = await _db.Orders.SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        if (order.Status != OrderStatus.Delivered)
        {
            throw new DomainConflictException("Cannot resend the delivery email unless the order is Delivered.");
        }

        var now = DateTimeOffset.UtcNow;
        var evt = new OutboxEvent(
            OutboxEventTypes.OrderDeliveryResendRequested,
            JsonSerializer.Serialize(new { orderId, requestedBy, requestedAt = now }),
            now);

        _db.OutboxEvents.Add(evt);
        await _db.SaveChangesAsync(cancellationToken);

        return evt.Id;
    }
}
