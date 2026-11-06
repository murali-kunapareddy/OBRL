using System.Threading.Channels;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Obrl.Web.Services.Email;

/// <summary>One email, ready to send: a plain-text body and the same text as simple HTML.</summary>
public sealed record OutgoingMail(string To, string? ToName, string Subject, string Text, string Html);

/// <summary>Sends email through the SMTP server in <see cref="ObrlOptions.Email"/>, with MailKit.</summary>
public class Mailer(IOptionsMonitor<ObrlOptions> options, ILogger<Mailer> logger)
{
    public EmailOptions Settings => options.CurrentValue.Email;

    public virtual bool IsConfigured => Settings.IsConfigured;

    /// <summary>Sends now. Throws when email is not set up or the mail server refuses the message.</summary>
    public virtual async Task SendAsync(OutgoingMail mail, CancellationToken ct = default)
    {
        var s = Settings;
        if (!s.IsConfigured) throw new InvalidOperationException("Email is not set up (Obrl:Email:Host and Obrl:Email:FromAddress).");

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(s.FromName, s.FromAddress));
        message.To.Add(new MailboxAddress(mail.ToName ?? "", mail.To));
        message.Subject = mail.Subject;
        message.Body = new BodyBuilder { TextBody = mail.Text, HtmlBody = mail.Html }.ToMessageBody();

        using var client = new SmtpClient { Timeout = Math.Max(5, s.TimeoutSeconds) * 1000 };
        await client.ConnectAsync(s.Host, s.Port, SecurityFor(s), ct);
        if (!string.IsNullOrWhiteSpace(s.Username))
            await client.AuthenticateAsync(s.Username, s.Password ?? "", ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
        logger.LogInformation("Email sent to {To}: {Subject}", mail.To, mail.Subject);
    }

    public static SecureSocketOptions SecurityFor(EmailOptions s) => s.Security?.Trim().ToLowerInvariant() switch
    {
        "starttls" => SecureSocketOptions.StartTls,
        "sslonconnect" or "ssl" or "tls" => SecureSocketOptions.SslOnConnect,
        "none" => SecureSocketOptions.None,
        _ => s.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable,
    };
}

/// <summary>
/// Emails waiting to go out. Pages add to it and return at once; <see cref="MailWorker"/> sends in the background,
/// so a slow or unreachable mail server never holds up an upload.
/// </summary>
public sealed class MailQueue
{
    private readonly Channel<OutgoingMail> _channel = Channel.CreateBounded<OutgoingMail>(
        new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public void Enqueue(OutgoingMail mail) => _channel.Writer.TryWrite(mail);

    public ChannelReader<OutgoingMail> Reader => _channel.Reader;
}

/// <summary>Sends queued emails one at a time. A failure is logged and written to the activity history; it is not retried.</summary>
public sealed class MailWorker(MailQueue queue, Mailer mailer, IServiceScopeFactory scopes, ILogger<MailWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var mail in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await mailer.SendAsync(mail, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Email to {To} could not be sent: {Subject}", mail.To, mail.Subject);
                try
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<ActivityLog>().WriteAsync(
                        Domain.ActivityTypes.EmailFailed, null, data: new { to = mail.To, subject = mail.Subject, error = ex.Message }, ct: stoppingToken);
                }
                catch (Exception logEx)
                {
                    logger.LogWarning(logEx, "Could not record the failed email in the activity history.");
                }
            }
        }
    }
}
