using Maxkeys.Application.Persistence;
using Maxkeys.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Maxkeys.Application.Payments;

/// <summary>
/// Fallback reconciliation for a payment MP approved but whose webhook notification never
/// arrived (dropped delivery, tunnel down, webhook killed mid-outage). Finds <see cref="Order"/>
/// rows stuck <see cref="OrderStatus.Pending"/> past <paramref name="staleAfter"/> in
/// <see cref="ExecuteAsync"/>, asks <see cref="IPaymentGateway.FindApprovedPaymentAsync"/> for
/// each, and replays any hit through <see cref="ProcessPaymentNotification"/> — the exact same
/// dedupe/state-guard/outbox-insert path a real webhook takes, so this job cannot double-approve
/// or double-insert an outbox row. Intended to run on a slow timer (see
/// <c>Maxkeys.Infrastructure.Payments.PaymentReconciliationService</c>), not per-request.
/// </summary>
public sealed class ReconcileStalePayments
{
    private readonly IAppDbContext _db;
    private readonly IPaymentGateway _paymentGateway;
    private readonly ProcessPaymentNotification _processPaymentNotification;
    private readonly ILogger<ReconcileStalePayments> _logger;

    public ReconcileStalePayments(
        IAppDbContext db,
        IPaymentGateway paymentGateway,
        ProcessPaymentNotification processPaymentNotification,
        ILogger<ReconcileStalePayments> logger)
    {
        _db = db;
        _paymentGateway = paymentGateway;
        _processPaymentNotification = processPaymentNotification;
        _logger = logger;
    }

    public async Task<ReconcileStalePaymentsResult> ExecuteAsync(
        TimeSpan staleAfter, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var cutoff = now - staleAfter;

        // MpPreferenceId is null only when preference creation itself failed — MP never saw
        // this order, so there is nothing to look up (design section 6a).
        var staleOrderIds = await _db.Orders
            .Where(o => o.Status == OrderStatus.Pending && o.CreatedAt < cutoff && o.MpPreferenceId != null)
            .Select(o => o.Id)
            .ToListAsync(cancellationToken);

        var reconciled = 0;
        var stillPending = 0;

        foreach (var orderId in staleOrderIds)
        {
            PaymentInfo? payment;
            try
            {
                payment = await _paymentGateway.FindApprovedPaymentAsync(orderId.ToString(), cancellationToken);
            }
            catch (PaymentGatewayException ex)
            {
                _logger.LogWarning(ex, "Reconciliation lookup failed for order {OrderId}; will retry next cycle.", orderId);
                stillPending++;
                continue;
            }

            if (payment is null)
            {
                stillPending++;
                continue;
            }

            var result = await _processPaymentNotification.ExecuteAsync(
                $"reconcile:{payment.Id}", payment.Id, cancellationToken);

            _logger.LogInformation(
                "Reconciliation replayed payment {PaymentId} for order {OrderId}: {Outcome}.",
                payment.Id, orderId, result.Outcome);

            if (result.Outcome == ProcessPaymentNotificationOutcome.Approved)
            {
                reconciled++;
            }
            else
            {
                stillPending++;
            }
        }

        return new ReconcileStalePaymentsResult(staleOrderIds.Count, reconciled, stillPending);
    }
}

/// <summary>One reconciliation pass's outcome — <see cref="ScannedCount"/> orders were stale, <see cref="ReconciledCount"/> got approved via a missed payment.</summary>
public sealed record ReconcileStalePaymentsResult(int ScannedCount, int ReconciledCount, int StillPendingCount);
