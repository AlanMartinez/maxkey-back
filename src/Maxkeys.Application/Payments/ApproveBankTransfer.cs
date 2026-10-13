using Maxkeys.Application.Fulfillment;
using Maxkeys.Application.Persistence;
using Maxkeys.Domain.Common;
using Maxkeys.Domain.Orders;
using Maxkeys.Domain.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Maxkeys.Application.Payments;

/// <summary>
/// Admin confirmation that a bank transfer arrived. <see cref="Order.ApproveTransfer"/> plus
/// an <see cref="OutboxEventTypes.OrderApproved"/> event in the same <c>SaveChangesAsync</c> —
/// exactly what <see cref="ProcessPaymentNotification"/> does for an approved Mercado Pago
/// payment, so vault auto-delivery and the operator email run unchanged from there.
/// Returns <see langword="null"/> for an unknown order (404); a wrong status, or losing a
/// concurrent approve/reject race, is a <see cref="DomainConflictException"/> (409).
/// </summary>
public sealed class ApproveBankTransfer
{
    private readonly IAppDbContext _db;
    private readonly ILogger<ApproveBankTransfer> _logger;

    public ApproveBankTransfer(IAppDbContext db, ILogger<ApproveBankTransfer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<AdminOrderSummary?> ExecuteAsync(Guid orderId, string approvedBy, CancellationToken cancellationToken = default)
    {
        var order = await BankTransferReview.LoadAsync(_db, orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        order.ApproveTransfer(now);
        _db.OutboxEvents.Add(new OutboxEvent(OutboxEventTypes.OrderApproved, $$"""{"orderId":"{{orderId}}"}""", now));

        await BankTransferReview.SaveAsync(_db, cancellationToken);

        _logger.LogInformation("Bank transfer for order {OrderId} approved by admin {AdminSub}.", orderId, approvedBy);

        return AdminOrderSummaryMapper.ToSummary(order);
    }
}

/// <summary>Load/save steps shared by <see cref="ApproveBankTransfer"/> and <see cref="RejectBankTransfer"/>.</summary>
internal static class BankTransferReview
{
    public static Task<Order?> LoadAsync(IAppDbContext db, Guid orderId, CancellationToken cancellationToken) =>
        db.Orders
            .Include(o => o.Items).ThenInclude(i => i.Keys)
            .SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);

    /// <summary>
    /// The order's <c>xmin</c> token makes a concurrent approve/reject (double click, two
    /// admins) lose with <see cref="DbUpdateConcurrencyException"/>; surfacing it as a 409
    /// matches what the loser would get had it read the already-updated status.
    /// </summary>
    public static async Task SaveAsync(IAppDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainConflictException("The order was modified concurrently; reload it and try again.");
        }
    }
}
