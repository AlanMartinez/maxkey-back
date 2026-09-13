namespace Maxkeys.Application.Payments;

/// <summary>
/// Raised by an <see cref="IPaymentGateway"/> implementation for any gateway
/// failure (network error, non-success HTTP status, kill switch). Maps to
/// HTTP 503 (design section 7 error table) — never a <c>DomainException</c>,
/// since it is not a business rule violation.
/// </summary>
public sealed class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message)
        : base(message)
    {
    }

    public PaymentGatewayException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
