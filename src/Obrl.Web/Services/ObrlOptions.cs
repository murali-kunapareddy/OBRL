namespace Obrl.Web.Services;

public class ObrlOptions
{
    public const string Section = "Obrl";

    /// <summary>Root folder for encrypted originals, derived content, covers and the page cache. Must be outside wwwroot.</summary>
    public string StorageRoot { get; set; } = "/var/lib/obrl";

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

    /// <summary>
    /// The domain passkeys are bound to, e.g. "library.example.org". Leave empty to use the host the site is
    /// served from. Changing it later makes existing passkeys stop working.
    /// </summary>
    public string? PasskeyServerDomain { get; set; }

    /// <summary>Name shown after "©" in the footer. Defaults to "OBRL".</summary>
    public string? CopyrightHolder { get; set; }

    public SeedOptions Seed { get; set; } = new();
}

public class SeedOptions
{
    /// <summary>First admin account, created at start-up when no admin exists. Set via environment variables.</summary>
    public string? AdminEmail { get; set; }
    public string? AdminPassword { get; set; }
}
