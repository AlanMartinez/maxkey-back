using Maxkeys.Application.Fulfillment;
using Maxkeys.Application.Persistence;
using Maxkeys.Domain.Orders;
using Microsoft.Extensions.Logging;

namespace Maxkeys.Application.Payments;

/// <summary>
/// Admin rejection of a bank transfer that never arrived: <see cref="Order.RejectTransfer"/>
/// cancels the order. No outbox event — nothing downstream reacts to a cancelled order.
/// Same 404/409 semantics as <see cref="ApproveBankTransfer"/>.
/// </summary>
public sealed class RejectBankTransfer
{
    private readonly IAppDbContext _db;
    private readonly ILogger<RejectBankTransfer> _logger;

    public RejectBankTransfer(IAppDbContext db, ILogger<RejectBankTransfer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<AdminOrderSummary?> ExecuteAsync(Guid orderId, string rejectedBy, CancellationToken cancellationToken = default)
    {
        var order = await BankTransferReview.LoadAsync(_db, orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        order.RejectTransfer(DateTimeOffset.UtcNow);

        await BankTransferReview.SaveAsync(_db, cancellationToken);

        _logger.LogInformation("Bank transfer for order {OrderId} rejected by admin {AdminSub}.", orderId, rejectedBy);

        return AdminOrderSummaryMapper.ToSummary(order);
    }
}
