using Maxkeys.Application.Notifications;
using Microsoft.Extensions.Logging;

namespace Maxkeys.Infrastructure.Email;

/// <summary>
/// Dev-default <see cref="IEmailSender"/> (design section 3; ADR-08) that logs
/// instead of sending. Subject and recipient are logged at
/// <see cref="LogLevel.Information"/>; the body only at
/// <see cref="LogLevel.Debug"/>. Never logs key material — the templates that
/// flow through this sender never contain any (see <c>EmailTemplates</c>).
/// </summary>
public sealed class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Email to {Recipient}: {Subject}", message.To, message.Subject);
        _logger.LogDebug("Email body for {Recipient}: {Body}", message.To, message.TextBody);

        return Task.CompletedTask;
    }
}
