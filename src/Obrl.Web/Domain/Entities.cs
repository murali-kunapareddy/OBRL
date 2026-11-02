using Microsoft.AspNetCore.Identity;

namespace Obrl.Web.Domain;

public class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = "";

    /// <summary>auto, light, dark or sepia.</summary>
    public string Theme { get; set; } = "auto";

    /// <summary>Id of the only session allowed to stay signed in. Changes at every sign-in.</summary>
    public string? CurrentSessionId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }
}

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int? ParentId { get; set; }
    public Category? Parent { get; set; }
    public List<Category> Children { get; set; } = [];

    /// <summary>
    /// Materialized path of ids, e.g. "/1/5/9/". Every descendant's path starts with this one,
    /// so "all books under this branch" is a single prefix match.
    /// </summary>
    public string Path { get; set; } = "/";

    public int SortOrder { get; set; }
    public List<BookCategory> Books { get; set; } = [];
}

public enum BookStatus
{
    Queued = 0,
    Processing = 1,
    Ready = 2,
    Failed = 3,
}

public enum ReaderMode
{
    /// <summary>Fixed layout: served as watermarked page images (PDF, Word, ...).</summary>
    Page = 0,

    /// <summary>Reflowable: served as cleaned HTML sections (EPUB, TXT, ...).</summary>
    Text = 1,
}

public class Book
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string? Author { get; set; }
    public string? Description { get; set; }
    public string OriginalFileName { get; set; } = "";

    /// <summary>Lower-case extension without the dot, e.g. "pdf", "epub".</summary>
    public string OriginalFormat { get; set; } = "";

    public long OriginalSize { get; set; }
    public ReaderMode Mode { get; set; }
    public BookStatus Status { get; set; } = BookStatus.Queued;
    public string? StatusMessage { get; set; }

    /// <summary>Pages for page mode, sections for text mode.</summary>
    public int UnitCount { get; set; }

    /// <summary>Text mode only: JSON list of section titles for the table of contents.</summary>
    public string? TocJson { get; set; }

    public bool HasCover { get; set; }
    public string UploadedById { get; set; } = "";
    public ApplicationUser? UploadedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadyAt { get; set; }
    public List<BookCategory> Categories { get; set; } = [];
}

public class BookCategory
{
    public Guid BookId { get; set; }
    public Book? Book { get; set; }
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
}

/// <summary>A user subscribes to exactly one of: a book, or a category (which includes all its sub categories).</summary>
public class Subscription
{
    public long Id { get; set; }
    public string UserId { get; set; } = "";
    public Guid? BookId { get; set; }
    public Book? Book { get; set; }
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class ReadingProgress
{
    public string UserId { get; set; } = "";
    public Guid BookId { get; set; }
    public Book? Book { get; set; }

    /// <summary>0-based page (page mode) or section (text mode).</summary>
    public int Position { get; set; }

    /// <summary>Scroll offset inside the page or section, 0..1.</summary>
    public double Offset { get; set; }

    public double PercentComplete { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class Bookmark
{
    public long Id { get; set; }
    public string UserId { get; set; } = "";
    public Guid BookId { get; set; }
    public Book? Book { get; set; }
    public int Position { get; set; }
    public double Offset { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class Disclaimer
{
    public int Id { get; set; }

    /// <summary>Increases every time an admin changes the text; users must accept the latest version.</summary>
    public int Version { get; set; }

    public string Text { get; set; } = "";
    public string? CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class TermsAcceptance
{
    public long Id { get; set; }
    public string UserId { get; set; } = "";
    public int DisclaimerVersion { get; set; }
    public DateTimeOffset AcceptedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? IpAddress { get; set; }
}

/// <summary>Append-only history of what users do. Feeds analytics in a later phase.</summary>
public class ActivityEvent
{
    public long Id { get; set; }
    public string? UserId { get; set; }
    public string Type { get; set; } = "";
    public Guid? BookId { get; set; }

    /// <summary>Optional JSON payload (stored as jsonb).</summary>
    public string? Data { get; set; }

    public string? IpAddress { get; set; }
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}

public static class ActivityTypes
{
    public const string Login = "login";
    public const string LoginFailed = "login_failed";
    public const string Logout = "logout";
    public const string BookOpened = "book_opened";
    public const string BookUploaded = "book_uploaded";
    public const string BookDeleted = "book_deleted";
    public const string Subscribed = "subscribed";
    public const string Unsubscribed = "unsubscribed";
    public const string BookmarkAdded = "bookmark_added";
    public const string DisclaimerAccepted = "disclaimer_accepted";
    public const string AccessDenied = "access_denied";
    public const string RateLimited = "rate_limited";
    public const string UserCreated = "user_created";
    public const string RoleChanged = "role_changed";
}
