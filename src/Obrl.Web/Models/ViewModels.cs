using System.ComponentModel.DataAnnotations;
using Obrl.Web.Domain;

namespace Obrl.Web.Models;

public sealed record BookCard(
    Guid Id,
    string Title,
    string? Author,
    bool HasCover,
    ReaderMode Mode,
    bool CanRead,
    double? Percent = null,
    double? Rating = null,
    string? Note = null);

public sealed class DashboardVm
{
    public List<BookCard> ContinueReading { get; init; } = [];
    public List<BookCard> Bookmarked { get; init; } = [];
    public List<BookCard> NewBooks { get; init; } = [];
    public List<BookCard> MostRead { get; init; } = [];
    public List<BookCard> TopRated { get; init; } = [];
    public List<BoardTopicRow> Board { get; init; } = [];
    public int SubscribedCategories { get; init; }
}

public sealed record BoardTopicRow(
    long Id,
    string Title,
    string AuthorName,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActivityAt,
    int Replies,
    bool IsPinned,
    bool IsLocked,
    bool IsHidden);

public sealed class BoardIndexVm
{
    public List<BoardTopicRow> Topics { get; init; } = [];
    public int Page { get; init; } = 1;
    public bool HasMore { get; init; }
    public bool CanModerate { get; init; }
}

public sealed record BoardPostView(long Id, string AuthorName, string Body, DateTimeOffset CreatedAt, bool IsHidden, bool IsMine);

public sealed class BoardTopicVm
{
    public required BoardTopicRow Topic { get; init; }
    public required BoardPostView Opening { get; init; }
    public List<BoardPostView> Replies { get; init; } = [];
    public bool CanModerate { get; init; }
    public bool CanReply { get; init; }
}

public sealed class NewTopicVm
{
    [Required, StringLength(200), Display(Name = "Title")]
    public string Title { get; set; } = "";

    [Required, StringLength(8000), Display(Name = "Message")]
    public string Body { get; set; } = "";
}

public sealed record PendingBookRow(
    Guid Id,
    string Title,
    string? Author,
    string Format,
    BookStatus Status,
    string UploadedBy,
    DateTimeOffset CreatedAt,
    bool HasCover);

public sealed record ReportedCommentRow(
    long CommentId,
    Guid BookId,
    string BookTitle,
    string AuthorName,
    string Body,
    bool IsHidden,
    DateTimeOffset CreatedAt,
    List<string> Reasons);

public sealed class ModerationVm
{
    public List<PendingBookRow> PendingBooks { get; init; } = [];
    public List<ReportedCommentRow> ReportedComments { get; init; } = [];
}

public sealed record CategoryNode(int Id, string Name, int? ParentId, string Path, int Depth, string? Description)
{
    public List<CategoryNode> Children { get; } = [];
}

public sealed class LibraryVm
{
    public List<CategoryNode> Roots { get; init; } = [];
    public CategoryNode? Current { get; init; }
    public List<CategoryNode> Breadcrumb { get; init; } = [];
    public HashSet<int> SubscribedIds { get; init; } = [];
    public List<string> SubscribedPaths { get; init; } = [];
    public List<BookCard> Books { get; init; } = [];
    public string? Query { get; init; }
    public bool MySubscriptionsOnly { get; init; }
    public int Page { get; init; } = 1;
    public bool HasMore { get; init; }
    public bool Limited { get; init; }
    public int SlotsInUse { get; init; }
    public int MaxBooks { get; init; }

    public bool IsCovered(CategoryNode c) => SubscribedPaths.Any(p => c.Path.StartsWith(p, StringComparison.Ordinal));
}

public sealed class BookDetailsVm
{
    public required Book Book { get; init; }

    /// <summary>Title, author and description in the member's language.</summary>
    public required Obrl.Web.Services.LocalizedBook Text { get; init; }

    /// <summary>For editors: the non-English languages the details were entered in.</summary>
    public List<string> TranslatedLanguages { get; init; } = [];
    public required List<List<CategoryNode>> CategoryTrails { get; init; }
    public bool CanRead { get; init; }

    /// <summary>The member's subscription to this book, active or ended; null when they never subscribed.</summary>
    public Subscription? MySubscription { get; init; }
    public bool SubscribedToBook => MySubscription is { } s && (s.ExpiresAt is null || s.ExpiresAt > DateTimeOffset.UtcNow);
    public bool SubscriptionEnded => MySubscription is { ExpiresAt: { } e } && e <= DateTimeOffset.UtcNow;

    /// <summary>The member uploaded this book (Contributors read their own books with no subscription and no end date).</summary>
    public bool OwnBook { get; init; }

    /// <summary>User or Contributor: the book limit and loan period apply.</summary>
    public bool Limited { get; init; }
    public int SlotsInUse { get; init; }
    public int MaxBooks { get; init; }
    public int LoanDays { get; init; }
    public bool CanEdit { get; init; }
    public ReadingProgress? Progress { get; init; }
    public List<Bookmark> Bookmarks { get; init; } = [];
    public int? MyRating { get; init; }
    public List<Obrl.Web.Services.CommentView> Comments { get; init; } = [];
    public bool CanModerate { get; init; }
}

public sealed class UploadVm
{
    public Guid? Id { get; set; }

    [Required, StringLength(300), Display(Name = "Title")]
    public string Title { get; set; } = "";

    [StringLength(300), Display(Name = "Author")]
    public string? Author { get; set; }

    [StringLength(4000), Display(Name = "Description")]
    public string? Description { get; set; }

    [Display(Name = "Categories")]
    public List<int> CategoryIds { get; set; } = [];

    /// <summary>The book's language, from the list Admins keep.</summary>
    [Display(Name = "Language")]
    public int? LanguageId { get; set; }

    /// <summary>A publisher from the list, or a new one (added to the list on save).</summary>
    [StringLength(200), Display(Name = "Publisher")]
    public string? PublisherName { get; set; }

    /// <summary>Year of publication. Checked in the controller: four digits, not in the future.</summary>
    [Display(Name = "Year of publication")]
    public int? PublishedYear { get; set; }

    [Display(Name = "Book file")]
    public IFormFile? File { get; set; }

    /// <summary>Where the cover comes from: the first page, a page the uploader picks, or an uploaded image.</summary>
    [Display(Name = "Cover")]
    public CoverSource CoverSource { get; set; }

    /// <summary>1-based page to use when <see cref="CoverSource"/> is Page.</summary>
    [Range(1, 100000), Display(Name = "Cover page")]
    public int? CoverPage { get; set; }

    [Display(Name = "Cover image")]
    public IFormFile? CoverImage { get; set; }

    // Shown on the edit page only.
    public BookStatus Status { get; set; }
    public ReaderMode? Mode { get; set; }
    public int PageCount { get; set; }
    public bool HasCover { get; set; }
    public bool HasCustomCover { get; set; }

    /// <summary>The chosen cover page has a saved crop.</summary>
    public bool HasPageCrop { get; set; }

    /// <summary>The cover page as cropped and adjusted in the browser (sent by "Crop and adjust").</summary>
    public IFormFile? PageCoverImage { get; set; }

    /// <summary>Go back to the whole chosen page, dropping its saved crop.</summary>
    public bool ClearPageCrop { get; set; }

    /// <summary>Title, author and description in the other interface languages, one entry per language.</summary>
    public List<BookTranslationInput> Translations { get; set; } = [];

    public List<CategoryNode> AllCategories { get; set; } = [];
    public List<BookLanguage> AllLanguages { get; set; } = [];
    public List<string> AllPublishers { get; set; } = [];

    /// <summary>One entry for every non-English language, filled from the book's saved translations or the posted form.</summary>
    public void FillTranslations(IEnumerable<BookTranslation> saved)
    {
        var posted = Translations;
        Translations = Obrl.Web.Localization.Languages.All
            .Where(l => l.Code != Obrl.Web.Localization.Languages.Default)
            .Select(l => posted.FirstOrDefault(p => p.Language == l.Code)
                ?? saved.Where(t => t.Language == l.Code)
                    .Select(t => new BookTranslationInput { Language = l.Code, Title = t.Title, Author = t.Author, Description = t.Description })
                    .FirstOrDefault()
                ?? new BookTranslationInput { Language = l.Code })
            .ToList();
    }
}

/// <summary>A book's details in one non-English language, as typed on the upload form. Every field is optional.</summary>
public sealed class BookTranslationInput
{
    public string? Language { get; set; }

    [StringLength(300), Display(Name = "Title")]
    public string? Title { get; set; }

    [StringLength(300), Display(Name = "Author")]
    public string? Author { get; set; }

    [StringLength(4000), Display(Name = "Description")]
    public string? Description { get; set; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Title) && string.IsNullOrWhiteSpace(Author) && string.IsNullOrWhiteSpace(Description);
}

public sealed class CategoryEditVm
{
    public int? Id { get; set; }

    [Required, StringLength(120), Display(Name = "Name")]
    public string Name { get; set; } = "";

    [StringLength(1000), Display(Name = "Description")]
    public string? Description { get; set; }

    [Display(Name = "Parent category")]
    public int? ParentId { get; set; }

    [Display(Name = "Sort order")]
    public int SortOrder { get; set; }

    /// <summary>Name and description in the other interface languages, one entry per language.</summary>
    public List<CategoryTranslationInput> Translations { get; set; } = [];

    public List<CategoryNode> AllCategories { get; set; } = [];

    /// <summary>One entry for every non-English language, filled from the category's saved translations or the posted form.</summary>
    public void FillTranslations(IEnumerable<CategoryTranslation> saved)
    {
        var posted = Translations;
        Translations = Obrl.Web.Localization.Languages.All
            .Where(l => l.Code != Obrl.Web.Localization.Languages.Default)
            .Select(l => posted.FirstOrDefault(p => p.Language == l.Code)
                ?? saved.Where(t => t.Language == l.Code)
                    .Select(t => new CategoryTranslationInput { Language = l.Code, Name = t.Name, Description = t.Description })
                    .FirstOrDefault()
                ?? new CategoryTranslationInput { Language = l.Code })
            .ToList();
    }
}

/// <summary>A category's name and description in one non-English language. Both are optional.</summary>
public sealed class CategoryTranslationInput
{
    public string? Language { get; set; }

    [StringLength(120), Display(Name = "Name")]
    public string? Name { get; set; }

    [StringLength(1000), Display(Name = "Description")]
    public string? Description { get; set; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Name) && string.IsNullOrWhiteSpace(Description);
}

public sealed class ReaderVm
{
    public required Book Book { get; init; }

    /// <summary>Title, author and description in the member's language.</summary>
    public required Obrl.Web.Services.LocalizedBook Text { get; init; }
    public int Position { get; init; }
    public double Offset { get; init; }
    public List<Bookmark> Bookmarks { get; init; } = [];
    public string TocJson { get; init; } = "[]";
    public required string UserLabel { get; init; }
    public required string UserIdShort { get; init; }
}

public sealed class CreateUserVm
{
    [Required, EmailAddress, StringLength(256), Display(Name = "Email")]
    public string Email { get; set; } = "";

    [Required, StringLength(100), Display(Name = "Display name")]
    public string DisplayName { get; set; } = "";

    [Required, Display(Name = "Role")]
    public string Role { get; set; } = Roles.User;

    /// <summary>Required unless the sign-in details are emailed, in which case an empty one is generated.</summary>
    [StringLength(128, MinimumLength = 12), DataType(DataType.Password), Display(Name = "Temporary password")]
    public string? Password { get; set; }

    /// <summary>Email the new member their address, temporary password and the sign-in link.</summary>
    public bool SendEmail { get; set; } = true;
}

public sealed record UserRow(
    string Id,
    string Email,
    string DisplayName,
    string Role,
    bool TwoFactor,
    int Passkeys,
    bool LockedOut,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);

public sealed class DisclaimerVm
{
    public required Disclaimer Disclaimer { get; init; }
    public string? ReturnUrl { get; init; }
}

/// <summary>Admin &gt; Settings.</summary>
public sealed record SettingsVm(int IdleMinutes, int LoanDays, int MaxBooks, EmailStatusVm Email);

/// <summary>The Email part of Admin &gt; Settings.</summary>
public sealed record EmailStatusVm(bool Configured, string? Server, string? From, bool NotifyUploads, bool WelcomeUsers);

/// <summary>Admin &gt; Book lists: the languages and publishers uploaders pick from.</summary>
public sealed class BookListsVm
{
    public List<BookListRow> Languages { get; init; } = [];
    public List<BookListRow> Publishers { get; init; } = [];
}

public sealed record BookListRow(int Id, string Name, int SortOrder, int Books);
