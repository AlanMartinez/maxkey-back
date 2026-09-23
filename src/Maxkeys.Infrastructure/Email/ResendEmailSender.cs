using Microsoft.Extensions.Options;
using Resend;
using EmailMessage = Maxkeys.Application.Notifications.EmailMessage;
using EmailOptions = Maxkeys.Application.Notifications.EmailOptions;
using IEmailSender = Maxkeys.Application.Notifications.IEmailSender;

namespace Maxkeys.Infrastructure.Email;

/// <summary>
/// <see cref="IEmailSender"/> backed by the Resend HTTP API (<c>Email:Resend:*</c>).
/// Selected over <see cref="SmtpEmailSender"/> when <c>Email:Sender</c> is
/// <c>Resend</c> — see <c>Infrastructure.DependencyInjection.AddEmailSender</c>,
/// which reads <see cref="EmailOptions.Sender"/> lazily via
/// <see cref="IOptionsMonitor{T}"/> at resolution time, same as
/// <see cref="SmtpEmailSender"/>. The <see cref="HttpClient"/> comes from
/// <c>AddHttpClient&lt;ResendEmailSender&gt;</c> (pooled/reused, unlike a
/// `new HttpClient()` per call) but <see cref="IResend"/> itself is still
/// built fresh per <see cref="SendAsync"/> call so a changed API key is
/// picked up without an app restart.
/// </summary>
public sealed class ResendEmailSender : IEmailSender
{
    private readonly IOptionsMonitor<EmailOptions> _options;
    private readonly HttpClient _httpClient;

    public ResendEmailSender(IOptionsMonitor<EmailOptions> options, HttpClient httpClient)
    {
        _options = options;
        _httpClient = httpClient;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var resend = ResendClient.Create(new ResendClientOptions { ApiToken = options.Resend.ApiKey }, _httpClient);

        var response = await resend.EmailSendAsync(
            new global::Resend.EmailMessage
            {
                From = options.From,
                To = message.To,
                Subject = message.Subject,
                TextBody = message.TextBody,
                HtmlBody = message.HtmlBody,
            },
            cancellationToken);

        if (!response.Success)
        {
            throw new InvalidOperationException(
                $"Resend send to {message.To} failed: {response.Exception?.ErrorType} {response.Exception?.Message}");
        }
    }
}
