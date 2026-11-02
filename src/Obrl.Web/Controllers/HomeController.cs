using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Obrl.Web.Data;
using Obrl.Web.Domain;
using Obrl.Web.Models;
using Obrl.Web.Services;

namespace Obrl.Web.Controllers;

public class HomeController(ObrlDbContext db, AccessService access) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var userId = AccessService.UserId(User);
        var readable = access.ReadableBooks(User);

        var continueReading = await db.ReadingProgress
            .Where(p => p.UserId == userId && p.PercentComplete < 99.5 && readable.Any(b => b.Id == p.BookId))
            .OrderByDescending(p => p.UpdatedAt).Take(12)
            .Select(p => new BookCard(p.BookId, p.Book!.Title, p.Book.Author, p.Book.HasCover, p.Book.Mode, true, p.PercentComplete))
            .ToListAsync(ct);

        var bookmarked = await readable
            .Where(b => db.Bookmarks.Any(m => m.UserId == userId && m.BookId == b.Id))
            .OrderByDescending(b => db.Bookmarks.Where(m => m.UserId == userId && m.BookId == b.Id).Max(m => m.CreatedAt))
            .Take(12)
            .Select(b => new BookCard(b.Id, b.Title, b.Author, b.HasCover, b.Mode, true, null))
            .ToListAsync(ct);

        var newBooks = await readable.OrderByDescending(b => b.ReadyAt).Take(12)
            .Select(b => new BookCard(b.Id, b.Title, b.Author, b.HasCover, b.Mode, true, null))
            .ToListAsync(ct);

        // Shelves the whole library shares. Locked books show too, so readers find what to subscribe to.
        var visible = access.VisibleBooks(User);

        var since = DateTimeOffset.UtcNow.AddDays(-30);
        var readers = await db.ActivityEvents.AsNoTracking()
            .Where(e => e.Type == ActivityTypes.BookOpened && e.At >= since && e.BookId != null)
            .GroupBy(e => e.BookId!.Value)
            .Select(g => new { BookId = g.Key, Readers = g.Select(e => e.UserId).Distinct().Count() })
            .OrderByDescending(x => x.Readers).Take(48)
            .ToListAsync(ct);
        var readerIds = readers.Select(r => r.BookId).ToList();
        var mostReadBooks = await visible.Where(b => readerIds.Contains(b.Id))
            .Select(b => new { b.Id, b.Title, b.Author, b.HasCover, b.Mode, b.RatingAverage, b.RatingCount, CanRead = readable.Any(r => r.Id == b.Id) })
            .ToDictionaryAsync(b => b.Id, ct);
        var mostRead = readers.Where(r => mostReadBooks.ContainsKey(r.BookId)).Take(12).Select(r =>
        {
            var b = mostReadBooks[r.BookId];
            return new BookCard(b.Id, b.Title, b.Author, b.HasCover, b.Mode, b.CanRead,
                Rating: b.RatingCount > 0 ? b.RatingAverage : null,
                Note: r.Readers == 1 ? "1 reader this month" : $"{r.Readers} readers this month");
        }).ToList();

        var libraryMean = await db.Ratings.AverageAsync(r => (double?)r.Stars, ct) ?? 3.0;
        const double w = Community.PriorWeight;
        var topRated = await visible.Where(b => b.RatingCount >= Community.TopRatedMinimumVotes)
            .OrderByDescending(b => (b.RatingAverage * b.RatingCount + libraryMean * w) / (b.RatingCount + w))
            .ThenByDescending(b => b.RatingCount)
            .Take(12)
            .Select(b => new BookCard(b.Id, b.Title, b.Author, b.HasCover, b.Mode, readable.Any(r => r.Id == b.Id), null,
                b.RatingAverage, b.RatingCount == 1 ? "1 rating" : b.RatingCount + " ratings"))
            .ToListAsync(ct);

        return View(new DashboardVm
        {
            ContinueReading = continueReading,
            Bookmarked = bookmarked,
            NewBooks = newBooks,
            MostRead = mostRead,
            TopRated = topRated,
            Board = await BoardController.TopicRows(db, AccessService.ReadsEverything(User)).Take(5).ToListAsync(ct),
            SubscribedCategories = await db.Subscriptions.CountAsync(s => s.UserId == userId && s.CategoryId != null, ct),
        });
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
