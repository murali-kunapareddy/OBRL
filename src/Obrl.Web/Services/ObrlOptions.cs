namespace Obrl.Web.Services;

public class ObrlOptions
{
    public const string Section = "Obrl";

    /// <summary>Root folder for encrypted originals, derived content, covers and the page cache. Must be outside wwwroot.</summary>
    public string StorageRoot { get; set; } = "/var/lib/obrl";

    /// <summary>
    /// Optional folder for the rendered-page cache and conversion scratch files. Empty uses <see cref="StorageRoot"/>.
    /// Set it to a local disk when <see cref="StorageRoot"/> is on another server (an NFS or SMB mount), so page
    /// rendering and conversions do not go over the network. The sign-in protection keys (keys/) are kept here too,
    /// so they never leave this server. Nothing in it needs a backup: losing keys/ only signs everyone out once.
    /// </summary>
    public string? CacheRoot { get; set; }

    /// <summary>File holding the 32-byte master key (base64). Created on first start if missing.</summary>
    public string KeyFile { get; set; } = "/etc/obrl/master.key";

    public string LibreOfficePath { get; set; } = "soffice";
    public string CalibreConvertPath { get; set; } = "ebook-convert";
    public int ConversionTimeoutSeconds { get; set; } = 600;

    public int MaxUploadMegabytes { get; set; } = 200;

    /// <summary>Widths (px) pages are rendered at. The reader asks for the smallest one that fills the screen.</summary>
    public int[] PageWidths { get; set; } = [800, 1200, 1600];

    public int PageUrlLifetimeSeconds { get; set; } = 60;

    /// <summary>Most pages or sections one user can load per minute.</summary>
    public int ReaderRequestsPerMinute { get; set; } = 60;

    /// <summary>Most pages or sections one user can load per hour.</summary>
    public int ReaderRequestsPerHour { get; set; } = 600;

    /// <summary>Minutes of inactivity before a user is signed out.</summary>
    public int IdleTimeoutMinutes { get; set; } = 30;

    /// <summary>Most books a User or Contributor can be subscribed to at once. Moderators and Admins have no limit.</summary>
    public int MaxSubscribedBooks { get; set; } = 5;

    /// <summary>
    /// The domain passkeys are bound to, e.g. "library.example.org". Leave empty to use the host the site is
    /// served from. Changing it later makes existing passkeys stop working.
    /// </summary>
    public string? PasskeyServerDomain { get; set; }

    /// <summary>Name shown after "©" in the footer. Defaults to "OBRL".</summary>
    public string? CopyrightHolder { get; set; }

    /// <summary>
    /// The address members open the library at, e.g. "https://books.example.org", used for links in emails.
    /// Empty uses the address of the page the email was sent from.
    /// </summary>
    public string? PublicUrl { get; set; }

    public SeedOptions Seed { get; set; } = new();

    public EmailOptions Email { get; set; } = new();
}

/// <summary>
/// The mail server OBRL sends email through (any SMTP server: your mail host, Gmail, Zoho, Brevo, a local Postfix...).
/// Email is off while <see cref="Host"/> or <see cref="FromAddress"/> is empty. Admins choose which emails go out
/// under Admin &gt; Settings. Keep <see cref="Password"/> out of appsettings.json: set it as the environment variable
/// <c>Obrl__Email__Password</c> (in /etc/obrl/obrl.env on the server).
/// </summary>
public class EmailOptions
{
    public string? Host { get; set; }

    /// <summary>587 for STARTTLS (most providers), 465 for SSL, 25 for a local relay.</summary>
    public int Port { get; set; } = 587;

    /// <summary>Auto (by port), StartTls, SslOnConnect or None (only for a relay on the same machine).</summary>
    public string Security { get; set; } = "Auto";

    /// <summary>Sign-in name for the mail server. Empty sends without signing in.</summary>
    public string? Username { get; set; }
    public string? Password { get; set; }

    /// <summary>The sender address, e.g. "library@example.org". Many providers only accept their own addresses.</summary>
    public string? FromAddress { get; set; }

    /// <summary>The sender name shown in the inbox.</summary>
    public string FromName { get; set; } = "OBRL";

    public int TimeoutSeconds { get; set; } = 30;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);
}

public class SeedOptions
{
    /// <summary>First admin account, created at start-up when no admin exists. Set via environment variables.</summary>
    public string? AdminEmail { get; set; }
    public string? AdminPassword { get; set; }
}
