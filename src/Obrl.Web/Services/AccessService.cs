using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Obrl.Web.Data;
using Obrl.Web.Domain;

namespace Obrl.Web.Services;

/// <summary>
/// Decides who may read which book.
/// <list type="bullet">
/// <item>Moderators and Admins can read every book.</item>
/// <item>Contributors can read the books they uploaded, with no subscription and no end date.</item>
/// <item>Everyone else reads a book while they hold an active subscription to it (one that has not passed its
/// end date). Users and Contributors can hold at most <see cref="Lending.MaxBooks"/> at once (see <see cref="Lending"/>).</item>
/// <item>Category subscriptions only follow a category (its new books show on the dashboard); they do not open books.</item>
/// <item>A book a Contributor uploads stays hidden from other readers until a Moderator approves it.</item>
/// </list>
/// </summary>
public sealed class AccessService(ObrlDbContext db, TimeProvider clock)
{
    public static bool ReadsEverything(ClaimsPrincipal user) =>
        user.IsInRole(Roles.Admin) || user.IsInRole(Roles.Moderator);

    /// <summary>Ready books the user may open.</summary>
    public IQueryable<Book> ReadableBooks(ClaimsPrincipal user) =>
        ReadableBooks(db, UserId(user), ReadsEverything(user), clock.GetUtcNow()).Where(b => b.Status == BookStatus.Ready);

    public static IQueryable<Book> ReadableBooks(ObrlDbContext db, string userId, bool readsEverything, DateTimeOffset now)
    {
        if (readsEverything) return db.Books;
        var active = ActiveBookSubscriptions(db, userId, now);
        return db.Books.Where(b =>
            b.UploadedById == userId
            || (b.IsApproved && active.Any(s => s.BookId == b.Id)));
    }

    /// <summary>The user's book subscriptions that have not reached their end date.</summary>
    public static IQueryable<Subscription> ActiveBookSubscriptions(ObrlDbContext db, string userId, DateTimeOffset now) =>
        db.Subscriptions.Where(s => s.UserId == userId && s.BookId != null && (s.ExpiresAt == null || s.ExpiresAt > now));

    /// <summary>
    /// Subscriptions that use up one of the member's book slots: active book subscriptions, apart from books the
    /// member uploaded (Contributors read their own books without a subscription).
    /// </summary>
    public static IQueryable<Subscription> SlotsInUse(ObrlDbContext db, string userId, DateTimeOffset now) =>
        ActiveBookSubscriptions(db, userId, now).Where(s => s.Book!.UploadedById != userId);

    /// <summary>Approved, ready books in the categories the user follows (or their sub categories), whether or not they can read them.</summary>
    public IQueryable<Book> FollowedBooks(ClaimsPrincipal user) => FollowedBooks(db, UserId(user));

    public static IQueryable<Book> FollowedBooks(ObrlDbContext db, string userId)
    {
        var followedPaths = db.Subscriptions
            .Where(s => s.UserId == userId && s.CategoryId != null)
            .Select(s => s.Category!.Path);
        return db.Books.Where(b => b.Status == BookStatus.Ready && b.IsApproved
            && b.Categories.Any(bc => followedPaths.Any(p => bc.Category!.Path.StartsWith(p))));
    }

    /// <summary>Ready books the user may see in the library (to browse and subscribe), whether or not they can read them yet.</summary>
    public IQueryable<Book> VisibleBooks(ClaimsPrincipal user) =>
        VisibleBooks(db, UserId(user), ReadsEverything(user)).Where(b => b.Status == BookStatus.Ready);

    public static IQueryable<Book> VisibleBooks(ObrlDbContext db, string userId, bool readsEverything) =>
        readsEverything ? db.Books : db.Books.Where(b => b.IsApproved || b.UploadedById == userId);

    public Task<bool> CanReadAsync(ClaimsPrincipal user, Guid bookId, CancellationToken ct = default) =>
        ReadableBooks(user).AnyAsync(b => b.Id == bookId, ct);

    /// <summary>Category ids the user is subscribed to directly.</summary>
    public async Task<HashSet<int>> SubscribedCategoryIdsAsync(string userId, CancellationToken ct = default) =>
        (await db.Subscriptions.Where(s => s.UserId == userId && s.CategoryId != null)
            .Select(s => s.CategoryId!.Value).ToListAsync(ct)).ToHashSet();

    /// <summary>Paths of the categories the user is subscribed to; a category is covered when its path starts with one of them.</summary>
    public async Task<List<string>> SubscribedPathsAsync(string userId, CancellationToken ct = default) =>
        await db.Subscriptions.Where(s => s.UserId == userId && s.CategoryId != null)
            .Select(s => s.Category!.Path).ToListAsync(ct);

    public static bool IsCovered(string categoryPath, IEnumerable<string> subscribedPaths) =>
        subscribedPaths.Any(p => categoryPath.StartsWith(p, StringComparison.Ordinal));

    public static string UserId(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("Not signed in.");
}
