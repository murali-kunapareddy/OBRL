using Microsoft.AspNetCore.Identity.UI.Services;

namespace Obrl.Web.Services;

/// <summary>
/// Phase 1 has no mail server: accounts are created by an admin with a confirmed email.
/// Messages Identity would send are written to the log instead. Swap in an SMTP sender later.
/// </summary>
public sealed class LogOnlyEmailSender(ILogger<LogOnlyEmailSender> logger) : IEmailSender
{
    public Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        logger.LogInformation("Email to {Email} not sent (no mail server configured): {Subject}", email, subject);
        return Task.CompletedTask;
    }
}
