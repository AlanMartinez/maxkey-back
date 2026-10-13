namespace Maxkeys.Domain.Orders;

/// <summary>
/// How the buyer pays for an <see cref="Order"/>. Persisted as text, so new values
/// can be added without renumbering. Orders created before bank transfers existed
/// default to <see cref="MercadoPago"/>.
/// </summary>
public enum PaymentMethod
{
    MercadoPago,
    BankTransfer
}
