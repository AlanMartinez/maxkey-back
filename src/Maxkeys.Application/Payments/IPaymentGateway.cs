using Maxkeys.Domain.Orders;

namespace Maxkeys.Application.Payments;

/// <summary>
/// Mercado Pago Checkout Pro seam (ADR-10). Implemented by
/// <c>Maxkeys.Infrastructure.Payments.MercadoPagoGateway</c> and, when
/// <c>Payments:AccessToken</c> is empty, <c>NotConfiguredPaymentGateway</c>
/// (design section 3/9, PR9). <see cref="FakePaymentGateway"/> in tests.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>
    /// Creates an MP preference for <paramref name="order"/> (design section 6a).
    /// Throws <see cref="PaymentGatewayException"/> on any gateway failure.
    /// </summary>
    Task<PaymentPreference> CreatePreferenceAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the authoritative payment state by id (design section 6b,
    /// "Authoritative Payment Fetch"). Throws <see cref="PaymentGatewayException"/>
    /// on any gateway failure.
    /// </summary>
    Task<PaymentInfo> GetPaymentAsync(string paymentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up an approved payment by <paramref name="externalReference"/> (the order id) for
    /// <see cref="ReconcileStalePayments"/> — covers a payment MP approved but whose webhook
    /// notification never arrived. Returns <see langword="null"/> when no approved payment is
    /// found. Throws <see cref="PaymentGatewayException"/> on any gateway failure.
    /// </summary>
    Task<PaymentInfo?> FindApprovedPaymentAsync(string externalReference, CancellationToken cancellationToken = default);
}

/// <summary>MP preference created for an order (design section 6a).</summary>
public sealed record PaymentPreference(string PreferenceId, string InitPoint);

/// <summary>
/// Authoritative payment state fetched from Mercado Pago (design section 6b).
/// <see cref="Status"/> is the raw MP status (<c>approved</c>, <c>rejected</c>,
/// <c>pending</c>, <c>in_process</c>, etc.) — interpreted by
/// <c>ProcessPaymentNotification</c>, never by the gateway.
/// </summary>
public sealed record PaymentInfo(
    string Id,
    string Status,
    string ExternalReference,
    decimal TransactionAmount,
    string CurrencyId);
