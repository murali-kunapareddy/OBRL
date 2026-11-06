using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Obrl.Web.Data;
using Obrl.Web.Domain;
using Obrl.Web.Localization;

namespace Obrl.Web.Services.Email;

/// <summary>
/// The emails OBRL sends. Each is written in the recipient's interface language (their account's choice), and
/// only when the mail server is set up and the Admin has that email turned on.
/// </summary>
public sealed class Notifications(
    ObrlDbContext db,
    Mailer mailer,
    MailQueue queue,
    EmailSettings settings,
    IOptions<ObrlOptions> options,
    IHttpContextAccessor http,
    IStringLocalizer<SharedResource> L,
    ILogger<Notifications> logger)
{
    public const string LoginPath = "/Identity/Account/Login?reason=welcome";

    public bool CanWelcomeUsers => mailer.IsConfigured && settings.WelcomeUsers;

    /// <summary>
    /// Tells every Moderator and Admin (except the uploader) about a new book. Queued: the upload does not wait for
    /// the mail server.
    /// </summary>
    public async Task NewUploadAsync(Book book, string uploaderId, CancellationToken ct = default)
    {
        if (!mailer.IsConfigured || !settings.NotifyUploads) return;
        var uploader = await db.Users.AsNoTracking().Where(u => u.Id == uploaderId)
            .Select(u => new { u.DisplayName, u.Email }).FirstOrDefaultAsync(ct);
        var uploaderName = string.IsNullOrWhiteSpace(uploader?.DisplayName) ? uploader?.Email ?? "?" : uploader.DisplayName;
        var now = DateTimeOffset.UtcNow;
        var recipients = await (from u in db.Users
                                join ur in db.UserRoles on u.Id equals ur.UserId
                                join r in db.Roles on ur.RoleId equals r.Id
                                where (r.Name == Roles.Moderator || r.Name == Roles.Admin)
                                      && u.Id != uploaderId && u.Email != null
                                      && (u.LockoutEnd == null || u.LockoutEnd < now)
                                select new { u.Email, u.DisplayName, u.Language }).Distinct().ToListAsync(ct);
        var link = book.IsApproved ? Link($"/books/{book.Id}") : Link("/moderation");
        foreach (var r in recipients)
        {
            using (UseLanguage(r.Language, Languages.Default))
            {
                var subject = book.IsApproved
                    ? L["New book: {0}", book.Title].Value
                    : L["New book waiting for approval: {0}", book.Title].Value;
                var body = new MailBody()
                    .Paragraph(L["Hello {0},", r.DisplayName].Value)
                    .Paragraph(L["{0} uploaded “{1}” ({2}, {3}).", uploaderName, book.Title, book.OriginalFormat.ToUpperInvariant(), Size(book.OriginalSize)].Value)
                    .Paragraph(book.IsApproved
                        ? L["It is in the library already, and readers see it once it is prepared."].Value
                        : L["Readers will see it once a Moderator approves it in the review queue."].Value)
                    .Button(book.IsApproved ? L["Open the book"].Value : L["Open the review queue"].Value, link)
                    .Small(L["You get this email because you are a Moderator or Admin. Admins can turn these emails off under Admin > Settings."].Value);
                queue.Enqueue(body.ToMail(r.Email!, r.DisplayName, subject));
            }
        }
    }

    /// <summary>
    /// Sends a member their email address, temporary password and the sign-in link. Sent straight away so the Admin
    /// sees whether it worked. Returns null when sent, or the reason it was not.
    /// </summary>
    public async Task<string?> WelcomeAsync(ApplicationUser user, string temporaryPassword, bool isReset, CancellationToken ct = default)
    {
        if (!CanWelcomeUsers) return L["Email is turned off."].Value;
        OutgoingMail mail;
        using (UseLanguage(user.Language, CultureInfo.CurrentUICulture.Name))
        {
            var subject = isReset ? L["Your new temporary password"].Value : L["Your library account is ready"].Value;
            mail = new MailBody()
                .Paragraph(L["Hello {0},", user.DisplayName].Value)
                .Paragraph(isReset
                    ? L["An administrator set a new temporary password for your library account."].Value
                    : L["An administrator created a library account for you."].Value)
                .Field(L["Email"].Value, user.Email!)
                .Field(L["Temporary password"].Value, temporaryPassword, monospace: true)
                .Button(L["Sign in"].Value, Link(LoginPath))
                .Paragraph(L["After signing in you choose your own password, then set up an authenticator app or a passkey. The temporary password stops working once you have changed it."].Value)
                .Small(L["If you did not expect this email, you can ignore it."].Value)
                .ToMail(user.Email!, user.DisplayName, subject);
        }
        try
        {
            await mailer.SendAsync(mail, ct);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Sign-in details for {Email} could not be emailed.", user.Email);
            return ex.Message;
        }
    }

    /// <summary>Sends a test email to the Admin, straight away. Returns null when sent, or the error.</summary>
    public async Task<string?> TestAsync(string to, string? name, CancellationToken ct = default)
    {
        if (!mailer.IsConfigured) return L["Email is not set up on the server."].Value;
        var mail = new MailBody()
            .Paragraph(L["This is a test email from the library. Email is working."].Value)
            .Button(L["Open the library"].Value, Link("/"))
            .ToMail(to, name, L["Test email"].Value);
        try
        {
            await mailer.SendAsync(mail, ct);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Test email to {Email} failed.", to);
            return ex.Message;
        }
    }

    /// <summary>A file size such as "850 KB" or "12.4 MB".</summary>
    public static string Size(long bytes) => bytes < 1024 * 1024
        ? $"{Math.Max(1, (int)Math.Round(bytes / 1024d))} KB"
        : $"{(bytes / 1024d / 1024d).ToString("0.#", CultureInfo.CurrentCulture)} MB";

    /// <summary>A random temporary password: 16 letters and digits without look-alikes (0/O, 1/l/I).</summary>
    public static string NewTemporaryPassword()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
        return RandomNumberGenerator.GetString(alphabet, 16);
    }

    /// <summary>A full link to a page of the library, from <c>Obrl:PublicUrl</c> or the current request.</summary>
    public string Link(string path)
    {
        var root = options.Value.PublicUrl;
        if (string.IsNullOrWhiteSpace(root) && http.HttpContext?.Request is { } req) root = $"{req.Scheme}://{req.Host}{req.PathBase}";
        return (root ?? "").TrimEnd('/') + path;
    }

    /// <summary>Writes the email in the member's language: their account's choice, else <paramref name="fallback"/>.</summary>
    private static CultureScope UseLanguage(string? code, string fallback) =>
        new(Languages.IsSupported(code) ? code! : Languages.IsSupported(fallback) ? fallback : Languages.Default);

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _culture = CultureInfo.CurrentCulture, _uiCulture = CultureInfo.CurrentUICulture;

        public CultureScope(string code)
        {
            var c = CultureInfo.GetCultureInfo(code);
            CultureInfo.CurrentCulture = c;
            CultureInfo.CurrentUICulture = c;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _culture;
            CultureInfo.CurrentUICulture = _uiCulture;
        }
    }
}

/// <summary>Builds an email's plain-text and HTML bodies side by side. All text is HTML-encoded.</summary>
public sealed class MailBody
{
    private readonly StringBuilder _text = new();
    private readonly StringBuilder _html = new();

    public MailBody Paragraph(string text)
    {
        _text.AppendLine(text).AppendLine();
        _html.Append("<p>").Append(Enc(text)).Append("</p>");
        return this;
    }

    public MailBody Field(string label, string value, bool monospace = false)
    {
        _text.Append(label).Append(": ").AppendLine(value);
        _html.Append("<p style=\"margin:4px 0\">").Append(Enc(label)).Append(": <strong")
            .Append(monospace ? " style=\"font-family:monospace;font-size:1.1em\"" : "").Append('>').Append(Enc(value)).Append("</strong></p>");
        return this;
    }

    public MailBody Button(string label, string url)
    {
        _text.AppendLine().Append(label).Append(": ").AppendLine(url).AppendLine();
        _html.Append("<p style=\"margin:20px 0\"><a href=\"").Append(Enc(url))
            .Append("\" style=\"background:#2f5d8a;color:#fff;padding:10px 18px;border-radius:6px;text-decoration:none;display:inline-block\">")
            .Append(Enc(label)).Append("</a></p><p style=\"font-size:12px;color:#666\">").Append(Enc(url)).Append("</p>");
        return this;
    }

    public MailBody Small(string text)
    {
        _text.AppendLine("--").AppendLine(text);
        _html.Append("<hr style=\"border:none;border-top:1px solid #ddd\"><p style=\"font-size:12px;color:#666\">").Append(Enc(text)).Append("</p>");
        return this;
    }

    public string Text => _text.ToString().TrimEnd() + "\n";

    public string Html =>
        "<!doctype html><html><body style=\"font-family:system-ui,-apple-system,'Segoe UI',sans-serif;font-size:15px;line-height:1.5;color:#222\">"
        + _html + "</body></html>";

    public OutgoingMail ToMail(string to, string? toName, string subject) => new(to, toName, subject, Text, Html);

    private static string Enc(string s) => WebUtility.HtmlEncode(s);
}
