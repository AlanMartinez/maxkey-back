using System.Collections.Concurrent;
using Maxkeys.Application.Payments;
using Maxkeys.Domain.Orders;
using Microsoft.Extensions.Options;

namespace Maxkeys.Infrastructure.Payments;

/// <summary>
/// Local demo <see cref="IPaymentGateway"/> (docs/local-demo.md), selected when
/// <c>Payments:Mode</c> is <c>Fake</c> and the host runs in Development.
/// <see cref="CreatePreferenceAsync"/> hands the buyer to the in-process
/// <c>/dev/payments/{orderId}</c> page instead of Mercado Pago; that page calls
/// <see cref="Approve"/>, after which <see cref="GetPaymentAsync"/> returns an
/// approved <see cref="PaymentInfo"/> whose external reference, amount and
/// currency match the order — exactly what <c>ProcessPaymentNotification</c>
/// needs to move the order to <c>Paid</c>. State is in-memory only (singleton)
/// and is lost on restart, which is fine for a demo: an order can simply be
/// approved again.
/// </summary>
public sealed class FakePaymentGateway : IPaymentGateway
{
    private const string PaymentIdPrefix = "fake-payment-";

    private readonly ConcurrentDictionary<Guid, PaymentInfo> _approvedPayments = new();
    private readonly IOptionsMonitor<MercadoPagoOptions> _options;

    public FakePaymentGateway(IOptionsMonitor<MercadoPagoOptions> options)
    {
        _options = options;
    }

    /// <summary>Deterministic payment id for an order, so the approval page and the gateway agree without shared state.</summary>
    public static string PaymentIdFor(Guid orderId) => $"{PaymentIdPrefix}{orderId}";

    public Task<PaymentPreference> CreatePreferenceAsync(Order order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        var baseUrl = _options.CurrentValue.FakeInitPointBaseUrl.TrimEnd('/');
        return Task.FromResult(new PaymentPreference(
            $"fake-{order.Id}",
            $"{baseUrl}/dev/payments/{order.Id}"));
    }

    public Task<PaymentInfo> GetPaymentAsync(string paymentId, CancellationToken cancellationToken = default)
    {
        if (paymentId.StartsWith(PaymentIdPrefix, StringComparison.Ordinal)
            && Guid.TryParse(paymentId[PaymentIdPrefix.Length..], out var orderId)
            && _approvedPayments.TryGetValue(orderId, out var payment))
        {
            return Task.FromResult(payment);
        }

        throw new PaymentGatewayException($"Fake payment '{paymentId}' has not been approved in this process.");
    }

    /// <summary>Records an approved payment for <paramref name="orderId"/>; idempotent.</summary>
    public PaymentInfo Approve(Guid orderId, decimal amount, string currency)
    {
        var payment = new PaymentInfo(PaymentIdFor(orderId), "approved", orderId.ToString(), amount, currency);
        _approvedPayments[orderId] = payment;
        return payment;
    }
}
