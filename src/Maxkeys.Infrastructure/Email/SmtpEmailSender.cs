using MailKit.Net.Smtp;
using MailKit.Security;
using Maxkeys.Application.Notifications;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Maxkeys.Infrastructure.Email;

/// <summary>
/// Production <see cref="IEmailSender"/> (ADR-08) using MailKit against a
/// generic SMTP server (<c>Email:Smtp:*</c>). Selected over
/// <see cref="LoggingEmailSender"/> when <c>Email:Sender</c> is <c>Smtp</c> —
/// see <c>Infrastructure.DependencyInjection.AddEmailSender</c>, which reads
/// <see cref="EmailOptions.Sender"/> lazily via <see cref="IOptionsMonitor{T}"/>
/// at resolution time, the same pattern used for payment gateway selection
/// (PR11). One connection per <see cref="SendAsync"/> call — MVP volume does
/// not justify a pooled/long-lived client.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly IOptionsMonitor<EmailOptions> _options;

    public SmtpEmailSender(IOptionsMonitor<EmailOptions> options)
    {
        _options = options;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;

        var mimeMessage = new MimeMessage();
        mimeMessage.From.Add(MailboxAddress.Parse(options.From));
        mimeMessage.To.Add(MailboxAddress.Parse(message.To));
        mimeMessage.Subject = message.Subject;

        var bodyBuilder = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody };
        mimeMessage.Body = bodyBuilder.ToMessageBody();

        using var client = new SmtpClient();

        var secureSocketOptions = options.Smtp.UseStartTls
            ? SecureSocketOptions.StartTls
            : SecureSocketOptions.Auto;

        await client.ConnectAsync(options.Smtp.Host, options.Smtp.Port, secureSocketOptions, cancellationToken);

        if (!string.IsNullOrWhiteSpace(options.Smtp.User))
        {
            await client.AuthenticateAsync(options.Smtp.User, options.Smtp.Password, cancellationToken);
        }

        await client.SendAsync(mimeMessage, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
