using Maxkeys.Application.Persistence;
using Maxkeys.Domain.Orders;
using Maxkeys.Domain.Outbox;
using Maxkeys.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Maxkeys.Application.Payments;

/// <summary>
/// Processes one Mercado Pago payment notification (design section 6b, payments-webhook
/// spec). Dedupes by <c>x-request-id</c>, always re-fetches the payment from the gateway,
/// and applies a state-guarded transition. Signature validation and the kill switch are
/// HTTP concerns owned by <c>WebhookEndpoints</c> (PR11); the caller already validated both.
/// </summary>
public sealed class ProcessPaymentNotification
{
    private readonly IAppDbContext _db;
    private readonly IPaymentGateway _paymentGateway;
    private readonly ILogger<ProcessPaymentNotification> _logger;

    public ProcessPaymentNotification(
        IAppDbContext db,
        IPaymentGateway paymentGateway,
        ILogger<ProcessPaymentNotification> logger)
    {
        _db = db;
        _paymentGateway = paymentGateway;
        _logger = logger;
    }

    public async Task<ProcessPaymentNotificationResult> ExecuteAsync(
        string requestId,
        string paymentId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            throw new ArgumentException("Request id must not be empty.", nameof(requestId));
        }

        if (string.IsNullOrWhiteSpace(paymentId))
        {
            throw new ArgumentException("Payment id must not be empty.", nameof(paymentId));
        }

        if (await _db.ProcessedWebhookNotifications.AnyAsync(n => n.RequestId == requestId, cancellationToken))
        {
            return ProcessPaymentNotificationResult.Duplicate;
        }

        // Authoritative fetch (spec: Authoritative Payment Fetch) — the body's status is never trusted.
        var payment = await _paymentGateway.GetPaymentAsync(paymentId, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        if (!Guid.TryParse(payment.ExternalReference, out var orderId))
        {
            return await RecordAsync(requestId, paymentId, null, ProcessPaymentNotificationResult.OrderNotFound, cancellationToken);
        }

        var order = await _db.Orders.SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        if (order is null)
        {
            return await RecordAsync(requestId, paymentId, orderId, ProcessPaymentNotificationResult.OrderNotFound, cancellationToken);
        }

        if (string.Equals(payment.Status, "approved", StringComparison.OrdinalIgnoreCase))
        {
            return await HandleApprovedAsync(requestId, paymentId, orderId, order, payment, now, cancellationToken);
        }

        if (payment.Status is "rejected" or "pending" or "in_process")
        {
            if (order.Status != OrderStatus.Pending)
            {
                return await RecordAsync(
                    requestId, paymentId, orderId, ProcessPaymentNotificationResult.Ignored("state_guard"), cancellationToken);
            }

            order.RecordPaymentAttempt(paymentId, payment.Status, now);
            _logger.LogInformation(
                "Payment {PaymentId} for order {OrderId} recorded as attempt ({Status}).", paymentId, orderId, payment.Status);
            return await RecordAsync(
                requestId, paymentId, orderId, ProcessPaymentNotificationResult.AttemptRecorded, cancellationToken);
        }

        _logger.LogWarning(
            "Payment {PaymentId} for order {OrderId} has non-actionable status {Status}; ignoring.", paymentId, orderId, payment.Status);
        return await RecordAsync(
            requestId, paymentId, orderId, ProcessPaymentNotificationResult.Ignored("non_actionable_status"), cancellationToken);
    }

    private async Task<ProcessPaymentNotificationResult> HandleApprovedAsync(
        string requestId, string paymentId, Guid orderId, Order order, PaymentInfo payment, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (order.Status != OrderStatus.Pending)
        {
            return await RecordAsync(
                requestId, paymentId, orderId, ProcessPaymentNotificationResult.Ignored("state_guard"), cancellationToken);
        }

        if (order.TotalAmount != payment.TransactionAmount ||
            !string.Equals(order.Currency, payment.CurrencyId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Payment {PaymentId} approved amount/currency mismatch for order {OrderId}.", paymentId, orderId);
            return await RecordAsync(
                requestId, paymentId, orderId, ProcessPaymentNotificationResult.Ignored("amount_mismatch"), cancellationToken);
        }

        order.MarkPaid(paymentId, now);
        _db.OutboxEvents.Add(new OutboxEvent(OutboxEventTypes.OrderApproved, $$"""{"orderId":"{{orderId}}"}""", now));

        return await RecordAsync(requestId, paymentId, orderId, ProcessPaymentNotificationResult.Approved, cancellationToken);
    }

    /// <summary>
    /// Persists any tracked domain change together with the dedupe row in one
    /// <c>SaveChangesAsync</c> (spec: Transactional Outbox Insert on Approval). On conflict —
    /// <c>xmin</c> or a unique-index violation from a concurrent duplicate delivery — the
    /// tracker is cleared and the outcome is re-derived from the order's current state, so
    /// exactly one transition and at most one outbox row ever survive.
    /// </summary>
    private async Task<ProcessPaymentNotificationResult> RecordAsync(
        string requestId, string paymentId, Guid? orderId, ProcessPaymentNotificationResult outcome,
        CancellationToken cancellationToken)
    {
        _db.ProcessedWebhookNotifications.Add(new ProcessedWebhookNotification(requestId, paymentId, orderId, DateTimeOffset.UtcNow));

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return outcome;
        }
        catch (DbUpdateException)
        {
            if (_db is DbContext dbContext)
            {
                dbContext.ChangeTracker.Clear();
            }

            if (orderId is null)
            {
                return ProcessPaymentNotificationResult.Duplicate;
            }

            var stillPending = await _db.Orders
                .Where(o => o.Id == orderId)
                .Select(o => o.Status == OrderStatus.Pending)
                .SingleOrDefaultAsync(cancellationToken);

            var retryOutcome = stillPending
                ? ProcessPaymentNotificationResult.Ignored("concurrent_conflict")
                : ProcessPaymentNotificationResult.Ignored("state_guard");
            return await RecordAsync(requestId, paymentId, orderId, retryOutcome, cancellationToken);
        }
    }
}

/// <summary>Outcome of <see cref="ProcessPaymentNotification.ExecuteAsync"/> (design section 6b).</summary>
public enum ProcessPaymentNotificationOutcome
{
    Duplicate,
    OrderNotFound,
    Ignored,
    AttemptRecorded,
    Approved
}

public sealed record ProcessPaymentNotificationResult(ProcessPaymentNotificationOutcome Outcome, string? Reason = null)
{
    public static readonly ProcessPaymentNotificationResult Duplicate = new(ProcessPaymentNotificationOutcome.Duplicate);
    public static readonly ProcessPaymentNotificationResult OrderNotFound = new(ProcessPaymentNotificationOutcome.OrderNotFound);
    public static readonly ProcessPaymentNotificationResult AttemptRecorded = new(ProcessPaymentNotificationOutcome.AttemptRecorded);
    public static readonly ProcessPaymentNotificationResult Approved = new(ProcessPaymentNotificationOutcome.Approved);

    public static ProcessPaymentNotificationResult Ignored(string reason) => new(ProcessPaymentNotificationOutcome.Ignored, reason);
}
