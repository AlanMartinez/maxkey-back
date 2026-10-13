namespace Maxkeys.Application.Checkout;

/// <summary>
/// Binds the <c>BankTransfer</c> configuration section: the account details shown to
/// a buyer who pays by bank transfer (<c>GET /checkout/bank-transfer</c>). Not secrets —
/// they are published to every buyer. The payment method is offered only while
/// <see cref="IsEnabled"/>, i.e. both <see cref="Alias"/> and <see cref="AccountHolder"/>
/// are set; <see cref="Cbu"/> is optional.
/// </summary>
public sealed class BankTransferOptions
{
    public const string SectionName = "BankTransfer";

    public string Alias { get; set; } = string.Empty;

    public string AccountHolder { get; set; } = string.Empty;

    public string? Cbu { get; set; }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(Alias) && !string.IsNullOrWhiteSpace(AccountHolder);
}

/// <summary>
/// A bank-transfer checkout was requested while <see cref="BankTransferOptions.IsEnabled"/>
/// is false. Maps to HTTP 503 "Bank transfer unavailable" — a configuration gap on our
/// side, not a caller error.
/// </summary>
public sealed class BankTransferUnavailableException : Exception
{
    public BankTransferUnavailableException()
        : base("Bank transfer payments are not configured.")
    {
    }
}
