using Amazon;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace FedTechSearch.Data.Email;

/// <summary>One email. <see cref="UnsubscribeUrl"/> becomes the RFC 8058 one-click unsubscribe headers.</summary>
public sealed record EmailMessage(string To, string Subject, string Html, string Text, string? UnsubscribeUrl = null);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

public sealed class EmailOptions
{
    public const string Section = "Email";

    /// <summary>Smtp (MailPit locally) or Ses (production).</summary>
    public string Provider { get; set; } = "Smtp";
    public string From { get; set; } = "FedTech Search <alerts@localhost>";
    public SmtpSettings Smtp { get; set; } = new();
    public SesSettings Ses { get; set; } = new();

    public sealed class SmtpSettings
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 1025;
        public bool UseTls { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
    }

    public sealed class SesSettings
    {
        public string Region { get; set; } = "us-east-1";
    }
}

/// <summary>Builds the MIME message both senders use: a text and an HTML part, plus unsubscribe headers.</summary>
public static class MimeBuilder
{
    public static MimeMessage Build(EmailMessage message, string from)
    {
        var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(from));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { TextBody = message.Text, HtmlBody = message.Html }.ToMessageBody();
        if (message.UnsubscribeUrl is { } url)
        {
            // One-click unsubscribe (RFC 8058): mail providers POST "List-Unsubscribe=One-Click" to the URL.
            mime.Headers.Add("List-Unsubscribe", $"<{url}>");
            mime.Headers.Add("List-Unsubscribe-Post", "List-Unsubscribe=One-Click");
        }
        return mime;
    }
}

/// <summary>Plain SMTP, used locally with MailPit (http://localhost:8025).</summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var o = options.Value;
        using var client = new SmtpClient();
        await client.ConnectAsync(o.Smtp.Host, o.Smtp.Port, o.Smtp.UseTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, cancellationToken);
        if (!string.IsNullOrEmpty(o.Smtp.Username))
            await client.AuthenticateAsync(o.Smtp.Username, o.Smtp.Password ?? "", cancellationToken);
        await client.SendAsync(MimeBuilder.Build(message, o.From), cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}

/// <summary>
/// Amazon SES (v2). Sends the raw MIME message so the List-Unsubscribe headers survive. Needs a
/// verified sending domain (SPF, DKIM, DMARC) and production access out of the SES sandbox.
/// Credentials come from the standard AWS chain (environment, profile, or task role).
/// </summary>
public sealed class SesEmailSender(IOptions<EmailOptions> options) : IEmailSender, IDisposable
{
    private readonly AmazonSimpleEmailServiceV2Client _client =
        new(RegionEndpoint.GetBySystemName(options.Value.Ses.Region));

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        await MimeBuilder.Build(message, options.Value.From).WriteToAsync(stream, cancellationToken);
        stream.Position = 0;
        await _client.SendEmailAsync(new SendEmailRequest
        {
            Content = new EmailContent { Raw = new RawMessage { Data = stream } }
        }, cancellationToken);
    }

    public void Dispose() => _client.Dispose();
}
