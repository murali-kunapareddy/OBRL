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
    [Required, StringLength(200)]
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

    public bool IsCovered(CategoryNode c) => SubscribedPaths.Any(p => c.Path.StartsWith(p, StringComparison.Ordinal));
}

public sealed class BookDetailsVm
{
    public required Book Book { get; init; }
    public required List<List<CategoryNode>> CategoryTrails { get; init; }
    public bool CanRead { get; init; }
    public bool SubscribedToBook { get; init; }
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

    [Required, StringLength(300)]
    public string Title { get; set; } = "";

    [StringLength(300)]
    public string? Author { get; set; }

    [StringLength(4000)]
    public string? Description { get; set; }

    [Display(Name = "Categories")]
    public List<int> CategoryIds { get; set; } = [];

    [Display(Name = "Book file")]
    public IFormFile? File { get; set; }

    public List<CategoryNode> AllCategories { get; set; } = [];
}

public sealed class CategoryEditVm
{
    public int? Id { get; set; }

    [Required, StringLength(120)]
    public string Name { get; set; } = "";

    [StringLength(1000)]
    public string? Description { get; set; }

    [Display(Name = "Parent category")]
    public int? ParentId { get; set; }

    public int SortOrder { get; set; }

    public List<CategoryNode> AllCategories { get; set; } = [];
}

public sealed class ReaderVm
{
    public required Book Book { get; init; }
    public int Position { get; init; }
    public double Offset { get; init; }
    public List<Bookmark> Bookmarks { get; init; } = [];
    public string TocJson { get; init; } = "[]";
    public required string UserLabel { get; init; }
    public required string UserIdShort { get; init; }
}

public sealed class CreateUserVm
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = "";

    [Required, StringLength(100), Display(Name = "Display name")]
    public string DisplayName { get; set; } = "";

    [Required]
    public string Role { get; set; } = Roles.User;

    [Required, StringLength(128, MinimumLength = 12), DataType(DataType.Password), Display(Name = "Temporary password")]
    public string Password { get; set; } = "";
}

public sealed record UserRow(
    string Id,
    string Email,
    string DisplayName,
    string Role,
    bool TwoFactor,
    bool LockedOut,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);

public sealed class DisclaimerVm
{
    public required Disclaimer Disclaimer { get; init; }
    public string? ReturnUrl { get; init; }
}
