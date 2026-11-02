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

        return View(new DashboardVm
        {
            ContinueReading = continueReading,
            Bookmarked = bookmarked,
            NewBooks = newBooks,
            SubscribedCategories = await db.Subscriptions.CountAsync(s => s.UserId == userId && s.CategoryId != null, ct),
        });
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
