namespace Maxkeys.Application.Notifications;

/// <summary>
/// Sends one <see cref="EmailMessage"/> (design section 3; ADR-08 — generic
/// SMTP is the production implementation; <c>LoggingEmailSender</c> is the dev
/// default). No interface method for batch sending — outbox handlers send one
/// message per event.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
