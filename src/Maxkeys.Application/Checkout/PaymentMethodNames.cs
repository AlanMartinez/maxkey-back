using Maxkeys.Domain.Common;
using Maxkeys.Domain.Orders;

namespace Maxkeys.Application.Checkout;

/// <summary>
/// Wire names for <see cref="PaymentMethod"/> (<c>"mercadopago"</c>, <c>"bank-transfer"</c>),
/// shared by the checkout request, the checkout status response and the admin order
/// responses so every endpoint spells them the same way.
/// </summary>
public static class PaymentMethodNames
{
    public const string MercadoPago = "mercadopago";
    public const string BankTransfer = "bank-transfer";

    public static string ToWire(PaymentMethod paymentMethod) => paymentMethod switch
    {
        PaymentMethod.BankTransfer => BankTransfer,
        _ => MercadoPago,
    };

    /// <summary>
    /// <see langword="null"/> means "not sent" and keeps existing clients on Mercado Pago;
    /// any other unrecognized value throws <see cref="DomainValidationException"/> on <c>paymentMethod</c>.
    /// </summary>
    public static PaymentMethod Parse(string? value)
    {
        if (value is null || string.Equals(value, MercadoPago, StringComparison.OrdinalIgnoreCase))
        {
            return PaymentMethod.MercadoPago;
        }

        if (string.Equals(value, BankTransfer, StringComparison.OrdinalIgnoreCase))
        {
            return PaymentMethod.BankTransfer;
        }

        throw new DomainValidationException(
            "paymentMethod", $"Payment method must be '{MercadoPago}' or '{BankTransfer}'.");
    }
}
