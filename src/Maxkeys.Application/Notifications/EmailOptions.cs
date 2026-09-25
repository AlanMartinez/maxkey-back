namespace Maxkeys.Application.Notifications;

/// <summary>
/// Binds the <c>Email</c> configuration section (design section 10; ADR-08).
/// <see cref="Sender"/> selects the <see cref="IEmailSender"/> implementation
/// *lazily*, at resolution time (<c>Logging</c> in dev, <c>Smtp</c> in prod —
/// <c>SmtpEmailSender</c> added in PR12), the same pattern used for the
/// payment gateway selection in <c>Infrastructure.DependencyInjection.AddPaymentGateway</c>.
/// <see cref="OperatorTo"/> is the single operator inbox that receives the
/// "awaiting fulfillment" notification. Named <c>OperatorTo</c>, not
/// <c>OperatorAddress</c> as design section 10's original config matrix named
/// it — the doc was reconciled to this name in PR12 (task 12.1).
/// <see cref="Sender"/> also accepts <c>Resend</c> (<see cref="ResendOptions"/>) —
/// same lazy-selection pattern, added alongside <c>Smtp</c> rather than
/// replacing it, since Resend's HTTP API (used here) gives clearer
/// send failures than going through its SMTP relay would.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string Sender { get; set; } = "Logging";

    public string From { get; set; } = string.Empty;

    public string OperatorTo { get; set; } = string.Empty;

    /// <summary>Buyer-facing link used by <c>EmailTemplates.BuyerOrderReady</c> (design section 3; admin-key-delivery-gate spec: decision 4 revisited — buyer notification on delivery).</summary>
    public string AccountOrdersUrl { get; set; } = string.Empty;

    public SmtpOptions Smtp { get; set; } = new();

    public ResendOptions Resend { get; set; } = new();
}

/// <summary>
/// Generic SMTP connection settings (ADR-08 — provider-agnostic; Resend,
/// SendGrid, Postmark, Gmail and Brevo all expose SMTP). Bound under
/// <c>Email:Smtp:*</c>; only read when <see cref="EmailOptions.Sender"/> is
/// <c>Smtp</c>.
/// </summary>
public sealed class SmtpOptions
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public bool UseStartTls { get; set; } = true;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}

/// <summary>Resend API settings. Bound under <c>Email:Resend:*</c>; only read when <see cref="EmailOptions.Sender"/> is <c>Resend</c>. <see cref="ApiKey"/> is a secret — set via <c>fly secrets</c>/user-secrets, never committed.</summary>
public sealed class ResendOptions
{
    public string ApiKey { get; set; } = string.Empty;
}
