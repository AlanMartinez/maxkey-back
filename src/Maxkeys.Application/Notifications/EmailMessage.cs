namespace Maxkeys.Application.Notifications;

/// <summary>
/// One outbound email (design section 3). <see cref="HtmlBody"/> is optional —
/// <c>LoggingEmailSender</c> and the operator template in this PR only need
/// <see cref="TextBody"/>; a future buyer-facing template (PR8) may add HTML.
/// </summary>
public sealed record EmailMessage(string To, string Subject, string TextBody, string? HtmlBody = null);
