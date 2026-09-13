using Maxkeys.Application.Payments;
using Maxkeys.Domain.Orders;

namespace Maxkeys.Infrastructure.Payments;

/// <summary>
/// Registered as <see cref="IPaymentGateway"/> when <c>Payments:AccessToken</c> is
/// empty (design section 3/9; PR9), so the API builds and runs green with no
/// Mercado Pago credentials configured. Every call throws
/// <see cref="PaymentGatewayException"/>, which the Problem Details handler maps
/// to 503 — matching the design's "credential-less green build" guarantee for
/// <c>POST /checkout/orders</c>. PR11 registers the real <c>MercadoPagoGateway</c>
/// instead once <c>Payments:AccessToken</c> is set.
/// </summary>
public sealed class NotConfiguredPaymentGateway : IPaymentGateway
{
    private const string Message = "Mercado Pago is not configured (Payments:AccessToken is empty).";

    public Task<PaymentPreference> CreatePreferenceAsync(Order order, CancellationToken cancellationToken = default) =>
        throw new PaymentGatewayException(Message);

    public Task<PaymentInfo> GetPaymentAsync(string paymentId, CancellationToken cancellationToken = default) =>
        throw new PaymentGatewayException(Message);
}
