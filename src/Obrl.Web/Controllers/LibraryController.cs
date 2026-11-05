using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Obrl.Web.Data;
using Obrl.Web.Domain;
using Obrl.Web.Models;
using Microsoft.Extensions.Localization;
using Obrl.Web.Services;

namespace Obrl.Web.Controllers;

/// <summary>Browse the category tree, search, and follow categories (a category subscription).</summary>
public class LibraryController(ObrlDbContext db, AccessService access, Lending lending, BookText bookText, ActivityLog log, TimeProvider clock, IStringLocalizer<SharedResource> L) : Controller
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

        // Sort by the title the member sees: the one in their language, or the English one.
        var lang = BookText.CurrentLanguage ?? "";
        System.Linq.Expressions.Expression<Func<Book, string>> shownTitle = b =>
            b.Translations.Where(t => t.Language == lang && t.Title != null).Select(t => t.Title).FirstOrDefault() ?? b.Title;

        IOrderedQueryable<Book> ordered;
        var search = q?.Trim();
        if (string.IsNullOrEmpty(search))
        {
            ordered = books.OrderBy(shownTitle);
        }
        else if (db.Database.IsNpgsql())
        {
            // Whole words anywhere in title, author or description (ranked), plus partial matches in title and author.
            var pattern = LikePattern(search);
            ordered = books
                .Where(b => b.SearchVector!.Matches(EF.Functions.WebSearchToTsQuery("simple", search))
                    || EF.Functions.ILike(b.Title, pattern)
                    || (b.Author != null && EF.Functions.ILike(b.Author, pattern))
                    // Details entered in other languages are searched too, whatever language the member reads in.
                    || b.Translations.Any(t => EF.Functions.ILike(t.Title!, pattern) || EF.Functions.ILike(t.Author!, pattern)
                        || EF.Functions.ILike(t.Description!, pattern)))
                .OrderByDescending(b => b.SearchVector!.Rank(EF.Functions.WebSearchToTsQuery("simple", search)))
                .ThenBy(shownTitle);
        }
        else
        {
            ordered = books.Where(b => b.Title.Contains(search) || (b.Author != null && b.Author.Contains(search))
                    || b.Translations.Any(t => (t.Title != null && t.Title.Contains(search)) || (t.Author != null && t.Author.Contains(search))))
                .OrderBy(shownTitle);
        }

        var rows = await ordered.Skip((page - 1) * PageSize).Take(PageSize + 1)
            .Select(b => new { b.Id, b.Title, b.Author, b.HasCover, b.Mode, b.RatingAverage, b.RatingCount, CanRead = readable.Any(r => r.Id == b.Id) })
            .ToListAsync(ct);

        // On "My subscriptions", say how long each book stays and which ones the member uploaded.
        var notes = new Dictionary<Guid, string>();
        if (mine && Lending.IsLimited(User))
        {
            var ids = rows.Select(r => r.Id).ToList();
            var now = clock.GetUtcNow();
            var ends = await AccessService.ActiveBookSubscriptions(db, userId, now).Where(x => ids.Contains(x.BookId!.Value))
                .Select(x => new { BookId = x.BookId!.Value, x.ExpiresAt }).ToListAsync(ct);
            foreach (var e in ends)
                if (e.ExpiresAt is { } until)
                    notes[e.BookId] = L["Until {0}", until.ToLocalTime().ToString("d MMM", System.Globalization.CultureInfo.CurrentCulture)].Value;
            var own = await db.Books.Where(b => ids.Contains(b.Id) && b.UploadedById == userId).Select(b => b.Id).ToListAsync(ct);
            foreach (var ownId in own) notes[ownId] = L["Uploaded by you"].Value;
        }

        return View(new LibraryVm
        {
            Roots = tree.Roots,
            Current = current,
            Breadcrumb = current is null ? [] : tree.Trail(current.Id),
            SubscribedIds = await access.SubscribedCategoryIdsAsync(userId, ct),
            SubscribedPaths = await access.SubscribedPathsAsync(userId, ct),
            Books = await bookText.LocalizeAsync(rows.Take(PageSize)
                .Select(r => new BookCard(r.Id, r.Title, r.Author, r.HasCover, r.Mode, r.CanRead, Rating: r.RatingCount > 0 ? r.RatingAverage : null,
                    Note: notes.GetValueOrDefault(r.Id))).ToList(), ct),
            Query = q,
            MySubscriptionsOnly = mine,
            Page = page,
            HasMore = rows.Count > PageSize,
            Limited = Lending.IsLimited(User),
            SlotsInUse = await lending.SlotsInUseAsync(db, userId, ct),
            MaxBooks = lending.MaxBooks,
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
