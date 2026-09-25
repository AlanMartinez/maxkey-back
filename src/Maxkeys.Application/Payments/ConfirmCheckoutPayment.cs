using Maxkeys.Application.Persistence;
using Maxkeys.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Payments;

/// <summary>
/// Reconciles one checkout-returned order without trusting redirect query parameters.
/// The gateway is searched by the durable order id, then the normal authoritative
/// payment pipeline validates amount/currency and persists the transition with its outbox event.
/// </summary>
public sealed class ConfirmCheckoutPayment
{
    private readonly IAppDbContext _db;
    private readonly IPaymentGateway _paymentGateway;
    private readonly ProcessPaymentNotification _processPaymentNotification;

    public ConfirmCheckoutPayment(
        IAppDbContext db,
        IPaymentGateway paymentGateway,
        ProcessPaymentNotification processPaymentNotification)
    {
        _db = db;
        _paymentGateway = paymentGateway;
        _processPaymentNotification = processPaymentNotification;
    }

    public async Task<ConfirmCheckoutPaymentResult> ExecuteAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await _db.Orders
            .Where(candidate => candidate.Id == orderId)
            .Select(candidate => new { candidate.Status, candidate.MpPreferenceId })
            .SingleOrDefaultAsync(cancellationToken);

        if (order is null)
        {
            return ConfirmCheckoutPaymentResult.NotFound;
        }

        if (order.Status != OrderStatus.Pending)
        {
            return ConfirmCheckoutPaymentResult.AlreadyProcessed;
        }

        if (order.MpPreferenceId is null)
        {
            return ConfirmCheckoutPaymentResult.Pending;
        }

        var payment = await _paymentGateway.FindApprovedPaymentAsync(orderId.ToString(), cancellationToken);
        if (payment is null)
        {
            return ConfirmCheckoutPaymentResult.Pending;
        }

        var result = await _processPaymentNotification.ExecuteAsync(
            $"checkout-return:{payment.Id}", payment.Id, cancellationToken);

        return result.Outcome switch
        {
            ProcessPaymentNotificationOutcome.Approved => ConfirmCheckoutPaymentResult.Approved,
            ProcessPaymentNotificationOutcome.Duplicate => ConfirmCheckoutPaymentResult.AlreadyProcessed,
            _ => ConfirmCheckoutPaymentResult.Pending,
        };
    }
}

public enum ConfirmCheckoutPaymentOutcome
{
    NotFound,
    Pending,
    Approved,
    AlreadyProcessed,
}

public sealed record ConfirmCheckoutPaymentResult(ConfirmCheckoutPaymentOutcome Outcome)
{
    public static readonly ConfirmCheckoutPaymentResult NotFound = new(ConfirmCheckoutPaymentOutcome.NotFound);
    public static readonly ConfirmCheckoutPaymentResult Pending = new(ConfirmCheckoutPaymentOutcome.Pending);
    public static readonly ConfirmCheckoutPaymentResult Approved = new(ConfirmCheckoutPaymentOutcome.Approved);
    public static readonly ConfirmCheckoutPaymentResult AlreadyProcessed = new(ConfirmCheckoutPaymentOutcome.AlreadyProcessed);
}
