using MailKit.Net.Smtp;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;

namespace Demo.Aspire.Notifications.Infrastructure;

public sealed class MailOptions
{
    public const string SectionName = "Mail";

    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 1025;

    public string FromAddress { get; set; } = "tickets@demo-kino.example";

    public string FromName { get; set; } = "Demo Kino";
}

public sealed record EmailMessage(string To, string Subject, string HtmlBody);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// Sends through Mailpit, the fourth container in the app host. It speaks real SMTP and
/// swallows everything, so the demo can show an actual delivered e-mail in a web inbox
/// instead of a log line claiming one was sent.
/// </summary>
internal sealed class SmtpEmailSender(IOptions<MailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;

        var mail = new MimeMessage
        {
            Subject = message.Subject,
            Body = new TextPart(TextFormat.Html) { Text = message.HtmlBody },
        };

        mail.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        mail.To.Add(MailboxAddress.Parse(message.To));

        using var client = new SmtpClient();

        await client.ConnectAsync(settings.Host, settings.Port, MailKit.Security.SecureSocketOptions.None, cancellationToken);
        await client.SendAsync(mail, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}
