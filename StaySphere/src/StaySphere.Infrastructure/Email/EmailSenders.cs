using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using StaySphere.Application.Abstractions;

namespace StaySphere.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string Section = "Email";
    /// <summary>Smtp (Mailpit locally; any SMTP relay / Azure Communication Services SMTP in prod) or Log.</summary>
    public string Provider { get; set; } = "Log";
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;
    public bool UseTls { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "no-reply@staysphere.local";
    public string FromName { get; set; } = "StaySphere";
}

public sealed class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var o = options.Value;
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(o.FromName, o.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody }.ToMessageBody();

        using var client = new SmtpClient { Timeout = 10_000 };
        await client.ConnectAsync(o.Host, o.Port, o.UseTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, cancellationToken);
        if (!string.IsNullOrEmpty(o.Username)) await client.AuthenticateAsync(o.Username, o.Password ?? string.Empty, cancellationToken);
        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
        logger.LogInformation("Email sent: {Subject}", message.Subject); // recipient intentionally not logged (PII)
    }
}

public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        logger.LogInformation("Email (not sent, Log provider): {Subject}", message.Subject);
        return Task.CompletedTask;
    }
}
