using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Obrl.Web.Data;
using Obrl.Web.Domain;
using Obrl.Web.Models;
using Obrl.Web.Services;

namespace Obrl.Web.Controllers;

/// <summary>Browse the category tree, search, and subscribe to categories.</summary>
public class LibraryController(ObrlDbContext db, AccessService access, ActivityLog log) : Controller
{
    private const int PageSize = 36;

    public async Task<IActionResult> Index(int? category, string? q, bool mine = false, int page = 1, CancellationToken ct = default)
    {
        var userId = AccessService.UserId(User);
        var tree = await CategoryTree.LoadAsync(db, ct);
        var current = category is int id ? tree.ById.GetValueOrDefault(id) : null;
        if (category is not null && current is null) return NotFound();
        page = Math.Max(1, page);

        var books = access.VisibleBooks(User).AsNoTracking();
        if (current is not null)
        {
            var path = current.Path;
            books = books.Where(b => b.Categories.Any(bc => bc.Category!.Path.StartsWith(path)));
        }

        var readable = access.ReadableBooks(User);
        if (mine) books = books.Where(b => readable.Any(r => r.Id == b.Id));

        IOrderedQueryable<Book> ordered;
        var search = q?.Trim();
        if (string.IsNullOrEmpty(search))
        {
            ordered = books.OrderBy(b => b.Title);
        }
        else if (db.Database.IsNpgsql())
        {
            // Whole words anywhere in title, author or description (ranked), plus partial matches in title and author.
            var pattern = LikePattern(search);
            ordered = books
                .Where(b => b.SearchVector!.Matches(EF.Functions.WebSearchToTsQuery("simple", search))
                    || EF.Functions.ILike(b.Title, pattern)
                    || (b.Author != null && EF.Functions.ILike(b.Author, pattern)))
                .OrderByDescending(b => b.SearchVector!.Rank(EF.Functions.WebSearchToTsQuery("simple", search)))
                .ThenBy(b => b.Title);
        }
        else
        {
            ordered = books.Where(b => b.Title.Contains(search) || (b.Author != null && b.Author.Contains(search))).OrderBy(b => b.Title);
        }

        var rows = await ordered.Skip((page - 1) * PageSize).Take(PageSize + 1)
            .Select(b => new { b.Id, b.Title, b.Author, b.HasCover, b.Mode, b.RatingAverage, b.RatingCount, CanRead = readable.Any(r => r.Id == b.Id) })
            .ToListAsync(ct);

        return View(new LibraryVm
        {
            Roots = tree.Roots,
            Current = current,
            Breadcrumb = current is null ? [] : tree.Trail(current.Id),
            SubscribedIds = await access.SubscribedCategoryIdsAsync(userId, ct),
            SubscribedPaths = await access.SubscribedPathsAsync(userId, ct),
            Books = rows.Take(PageSize).Select(r => new BookCard(r.Id, r.Title, r.Author, r.HasCover, r.Mode, r.CanRead, Rating: r.RatingCount > 0 ? r.RatingAverage : null)).ToList(),
            Query = q,
            MySubscriptionsOnly = mine,
            Page = page,
            HasMore = rows.Count > PageSize,
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Subscribe(int id, string? returnUrl, CancellationToken ct)
    {
        var userId = AccessService.UserId(User);
        if (!await db.Categories.AnyAsync(c => c.Id == id, ct)) return NotFound();
        if (!await db.Subscriptions.AnyAsync(s => s.UserId == userId && s.CategoryId == id, ct))
        {
            db.Subscriptions.Add(new Subscription { UserId = userId, CategoryId = id });
            log.Add(ActivityTypes.Subscribed, userId, data: new { categoryId = id });
            await db.SaveChangesAsync(ct);
        }
        return LocalRedirectOrLibrary(returnUrl, id);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Unsubscribe(int id, string? returnUrl, CancellationToken ct)
    {
        var userId = AccessService.UserId(User);
        var removed = await db.Subscriptions.Where(s => s.UserId == userId && s.CategoryId == id).ExecuteDeleteAsync(ct);
        if (removed > 0) await log.WriteAsync(ActivityTypes.Unsubscribed, userId, data: new { categoryId = id }, ct: ct);
        return LocalRedirectOrLibrary(returnUrl, id);
    }

    private static string LikePattern(string text) =>
        "%" + text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    private IActionResult LocalRedirectOrLibrary(string? returnUrl, int category) =>
        Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction(nameof(Index), new { category });
}
