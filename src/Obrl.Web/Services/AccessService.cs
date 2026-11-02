using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Obrl.Web.Data;
using Obrl.Web.Domain;

namespace Obrl.Web.Services;

/// <summary>
/// Decides who may read which book.
/// <list type="bullet">
/// <item>Moderators and Admins can read every book.</item>
/// <item>Contributors can read the books they uploaded.</item>
/// <item>Everyone can read books they subscribed to, and every book in a subscribed category
/// or any of its sub categories (at any depth).</item>
/// </list>
/// </summary>
public sealed class AccessService(ObrlDbContext db)
{
    public static bool ReadsEverything(ClaimsPrincipal user) =>
        user.IsInRole(Roles.Admin) || user.IsInRole(Roles.Moderator);

    /// <summary>Ready books the user may open.</summary>
    public IQueryable<Book> ReadableBooks(ClaimsPrincipal user) =>
        ReadableBooks(db, UserId(user), ReadsEverything(user)).Where(b => b.Status == BookStatus.Ready);

    public static IQueryable<Book> ReadableBooks(ObrlDbContext db, string userId, bool readsEverything)
    {
        if (readsEverything) return db.Books;

        var subscribedPaths = db.Subscriptions
            .Where(s => s.UserId == userId && s.CategoryId != null)
            .Select(s => s.Category!.Path);

        return db.Books.Where(b =>
            b.UploadedById == userId
            || db.Subscriptions.Any(s => s.UserId == userId && s.BookId == b.Id)
            || b.Categories.Any(bc => subscribedPaths.Any(p => bc.Category!.Path.StartsWith(p))));
    }

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
