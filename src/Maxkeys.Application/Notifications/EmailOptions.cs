namespace Maxkeys.Application.Notifications;

/// <summary>
/// Binds the <c>Email</c> configuration section (design section 10; ADR-08).
/// <see cref="Sender"/> selects the <see cref="IEmailSender"/> implementation
/// at DI registration time (<c>Logging</c> in dev, <c>Smtp</c> in prod — wired
/// in PR12 once <c>SmtpEmailSender</c> exists). <see cref="OperatorTo"/> is the
/// single operator inbox that receives the "awaiting fulfillment" notification.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string Sender { get; set; } = "Logging";

    public string From { get; set; } = string.Empty;

    public string OperatorTo { get; set; } = string.Empty;
}
