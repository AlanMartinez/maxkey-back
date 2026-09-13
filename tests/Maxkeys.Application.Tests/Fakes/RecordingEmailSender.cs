using Maxkeys.Application.Notifications;

namespace Maxkeys.Application.Tests.Fakes;

/// <summary>In-memory <see cref="IEmailSender"/> for Application tests — records every message sent, sends nothing for real.</summary>
public sealed class RecordingEmailSender : IEmailSender
{
    public List<EmailMessage> SentMessages { get; } = [];

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        SentMessages.Add(message);
        return Task.CompletedTask;
    }
}
