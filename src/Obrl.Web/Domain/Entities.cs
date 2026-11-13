using Microsoft.AspNetCore.Identity;

namespace Obrl.Web.Domain;

public class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = "";

    /// <summary>auto, light, dark or sepia.</summary>
    public string Theme { get; set; } = "auto";

    /// <summary>Interface language code, e.g. "en", "te" or "kn". Null until the member picks one.</summary>
    public string? Language { get; set; }

    /// <summary>Id of the only session allowed to stay signed in. Changes at every sign-in.</summary>
    public string? CurrentSessionId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>
    /// Set when an Admin gives the account a temporary password (new account or password reset). Until the member
    /// picks their own password, every page sends them to the change password page.
    /// </summary>
    public bool MustChangePassword { get; set; }
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

    /// <summary>Name and description in other interface languages. The fields above are the English text.</summary>
    public List<CategoryTranslation> Translations { get; set; } = [];
}

/// <summary>
/// A category's name and description in one non-English interface language. Each field is optional: an empty one shows the English text.
/// </summary>
public class CategoryTranslation
{
    public int CategoryId { get; set; }
    public Category? Category { get; set; }

    /// <summary>Language code from <see cref="Obrl.Web.Localization.Languages"/>, e.g. "te".</summary>
    public string Language { get; set; } = "";

    public string? Name { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Where a book's cover comes from.</summary>
public enum CoverSource
{
    /// <summary>The first page (PDF, Word), or the e-book's own cover or one made from the title (EPUB, text).</summary>
    Default = 0,

    /// <summary>A page the uploader picked (<see cref="Book.CoverPage"/>). Page-mode books only.</summary>
    Page = 1,

    /// <summary>An image the uploader supplied.</summary>
    Custom = 2,
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

    /// <summary>The language the book is written in, from the list Admins keep. Null for books from before the field existed.</summary>
    public int? LanguageId { get; set; }
    public BookLanguage? Language { get; set; }

    public int? PublisherId { get; set; }
    public Publisher? Publisher { get; set; }

    /// <summary>Year of publication, e.g. 1998. Never later than the current year.</summary>
    public int? PublishedYear { get; set; }

    /// <summary>Where the cover comes from. If the choice can't be used (no image, page out of range) the default is used.</summary>
    public CoverSource CoverSource { get; set; }

    /// <summary>1-based page used as the cover when <see cref="CoverSource"/> is <see cref="CoverSource.Page"/>.</summary>
    public int? CoverPage { get; set; }

    /// <summary>
    /// Books from Contributors wait for a Moderator before readers can see them.
    /// Books from Moderators and Admins are approved on upload.
    /// </summary>
    public bool IsApproved { get; set; } = true;
    public string? ApprovedById { get; set; }

    /// <summary>Set when a Moderator turns the book down; cleared when the uploader edits it to resubmit.</summary>
    public DateTimeOffset? RejectedAt { get; set; }

    /// <summary>The Moderator's note to the uploader when rejecting.</summary>
    public string? ReviewNote { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }

    /// <summary>Kept in step with the Ratings table so shelves can sort by it cheaply.</summary>
    public double RatingAverage { get; set; }
    public int RatingCount { get; set; }

    public string UploadedById { get; set; } = "";
    public ApplicationUser? UploadedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadyAt { get; set; }
    public List<BookCategory> Categories { get; set; } = [];

    /// <summary>Title, author and description in other interface languages. The fields above are the English text.</summary>
    public List<BookTranslation> Translations { get; set; } = [];

    /// <summary>Generated by PostgreSQL from title, author and description; used for search.</summary>
    public NpgsqlTypes.NpgsqlTsVector? SearchVector { get; set; }
}

/// <summary>
/// A book's details in one non-English interface language. Each field is optional: an empty one shows the English text.
/// </summary>
public class BookTranslation
{
    public Guid BookId { get; set; }
    public Book? Book { get; set; }

    /// <summary>Language code from <see cref="Obrl.Web.Localization.Languages"/>, e.g. "te".</summary>
    public string Language { get; set; } = "";

    public string? Title { get; set; }
    public string? Author { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>A language a book can be written in. Admins keep the list (Admin &gt; Book lists).</summary>
public class BookLanguage
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    /// <summary>Upper-case name, for the unique check (so "telugu" and "Telugu" are the same language).</summary>
    public string NormalizedName { get; set; } = "";
    public int SortOrder { get; set; }
}

/// <summary>A publisher or publication. Uploaders pick one from the list or type a new one, which is added to the list.</summary>
public class Publisher
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    /// <summary>Upper-case name with single spaces, for the unique check.</summary>
    public string NormalizedName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>The publisher's name in other interface languages. English (<see cref="Name"/>) is required; the rest are optional.</summary>
    public List<PublisherTranslation> Translations { get; set; } = [];

    public static string Normalize(string name) =>
        string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
}

/// <summary>A publisher's name in one non-English interface language. Readers of other languages see the English name.</summary>
public class PublisherTranslation
{
    public int PublisherId { get; set; }
    public Publisher? Publisher { get; set; }

    /// <summary>Language code from <see cref="Obrl.Web.Localization.Languages"/>, e.g. "te".</summary>
    public string Language { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class BookCategory
{
    public Guid BookId { get; set; }
    public Book? Book { get; set; }
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
}

/// <summary>
/// A user subscribes to exactly one of: a book, or a category (which includes all its sub categories).
/// A book subscription lets the member read the book; a category subscription means following it: its new books
/// show on the member's dashboard, but each book still needs its own subscription to read.
/// </summary>
public class Subscription
{
    public long Id { get; set; }
    public string UserId { get; set; } = "";
    public Guid? BookId { get; set; }
    public Book? Book { get; set; }
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Book subscriptions of Users and Contributors end at this time (the loan period an Admin picks), after which
    /// the book needs a new subscription. Null means no end: category subscriptions, and books of Moderators and Admins
    /// or subscribed while the loan period was off.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; set; }
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

/// <summary>A library-wide setting that Admins change in the app (key/value).</summary>
public class SiteSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? UpdatedById { get; set; }
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

/// <summary>One rating per user per book, 1 to 5 stars.</summary>
public class Rating
{
    public string UserId { get; set; } = "";
    public Guid BookId { get; set; }
    public Book? Book { get; set; }
    public int Stars { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>A comment on a book. Replies point at their parent (one level deep).</summary>
public class Comment
{
    public long Id { get; set; }
    public Guid BookId { get; set; }
    public Book? Book { get; set; }
    public string UserId { get; set; } = "";
    public ApplicationUser? User { get; set; }
    public long? ParentId { get; set; }
    public Comment? Parent { get; set; }
    public List<Comment> Replies { get; set; } = [];
    public string Body { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EditedAt { get; set; }

    /// <summary>Hidden by a moderator: kept for the record, not shown to readers.</summary>
    public bool IsHidden { get; set; }
    public string? HiddenById { get; set; }
    public string? HiddenReason { get; set; }
    public List<CommentReport> Reports { get; set; } = [];
}

/// <summary>A reader flags a comment for moderators. One report per user per comment.</summary>
public class CommentReport
{
    public long Id { get; set; }
    public long CommentId { get; set; }
    public Comment? Comment { get; set; }
    public string UserId { get; set; } = "";
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Set when a moderator has dealt with the report (kept or hid the comment).</summary>
    public DateTimeOffset? ResolvedAt { get; set; }
}

/// <summary>A message board post, or a reply to one (ParentId set).</summary>
public class BoardPost
{
    public long Id { get; set; }
    public long? ParentId { get; set; }
    public BoardPost? Parent { get; set; }
    public List<BoardPost> Replies { get; set; } = [];
    public string UserId { get; set; } = "";
    public ApplicationUser? User { get; set; }

    /// <summary>Topics have a title; replies do not.</summary>
    public string? Title { get; set; }

    public string Body { get; set; } = "";
    public bool IsPinned { get; set; }
    public bool IsLocked { get; set; }
    public bool IsHidden { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>For topics: time of the latest reply, used to sort the board.</summary>
    public DateTimeOffset LastActivityAt { get; set; } = DateTimeOffset.UtcNow;
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
    public const string SubscriptionLimitReached = "subscription_limit_reached";
    public const string BookmarkAdded = "bookmark_added";
    public const string DisclaimerAccepted = "disclaimer_accepted";
    public const string AccessDenied = "access_denied";
    public const string RateLimited = "rate_limited";
    public const string UserCreated = "user_created";
    public const string RoleChanged = "role_changed";
    public const string BookApproved = "book_approved";
    public const string BookRejected = "book_rejected";
    public const string Rated = "rated";
    public const string Commented = "commented";
    public const string CommentDeleted = "comment_deleted";
    public const string CommentReported = "comment_reported";
    public const string CommentHidden = "comment_hidden";
    public const string BoardPosted = "board_posted";
    public const string BoardModerated = "board_moderated";
    public const string PasskeyAdded = "passkey_added";
    public const string PasskeyRemoved = "passkey_removed";
    public const string IdleSignOut = "idle_sign_out";
    public const string SettingChanged = "setting_changed";
    public const string PasswordChanged = "password_changed";
    public const string EmailSent = "email_sent";
    public const string EmailFailed = "email_failed";
}
