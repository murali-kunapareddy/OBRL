using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Obrl.Web.Data;
using Obrl.Web.Domain;
using Obrl.Web.Models;
using Obrl.Web.Services;

namespace Obrl.Web.Controllers;

public class BooksController(ObrlDbContext db, AccessService access, BookStorage storage, ActivityLog log) : Controller
{
    [HttpGet("/books/{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var userId = AccessService.UserId(User);
        var book = await db.Books.AsNoTracking().Include(b => b.Categories).FirstOrDefaultAsync(b => b.Id == id, ct);
        if (book is null) return NotFound();
        var canEdit = CanEdit(book);
        if (book.Status != BookStatus.Ready && !canEdit) return NotFound();

        var tree = await CategoryTree.LoadAsync(db, ct);
        return View(new BookDetailsVm
        {
            Book = book,
            CategoryTrails = book.Categories.Select(c => tree.Trail(c.CategoryId)).Where(t => t.Count > 0).ToList(),
            CanRead = book.Status == BookStatus.Ready && await access.CanReadAsync(User, id, ct),
            SubscribedToBook = await db.Subscriptions.AnyAsync(s => s.UserId == userId && s.BookId == id, ct),
            CanEdit = canEdit,
            Progress = await db.ReadingProgress.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId && p.BookId == id, ct),
            Bookmarks = await db.Bookmarks.AsNoTracking().Where(m => m.UserId == userId && m.BookId == id)
                .OrderBy(m => m.Position).ThenBy(m => m.Offset).ToListAsync(ct),
        });
    }

    /// <summary>Cover thumbnails are visible to every signed-in user, so they can browse what to subscribe to.</summary>
    [HttpGet("/books/{id:guid}/cover")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Client)]
    public IActionResult Cover(Guid id)
    {
        var path = storage.CoverPath(id);
        return System.IO.File.Exists(path) ? PhysicalFile(path, "image/webp") : NotFound();
    }

    [HttpPost("/books/{id:guid}/subscribe"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Subscribe(Guid id, CancellationToken ct)
    {
        var userId = AccessService.UserId(User);
        if (!await db.Books.AnyAsync(b => b.Id == id && b.Status == BookStatus.Ready, ct)) return NotFound();
        if (!await db.Subscriptions.AnyAsync(s => s.UserId == userId && s.BookId == id, ct))
        {
            db.Subscriptions.Add(new Subscription { UserId = userId, BookId = id });
            log.Add(ActivityTypes.Subscribed, userId, id);
            await db.SaveChangesAsync(ct);
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("/books/{id:guid}/unsubscribe"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Unsubscribe(Guid id, CancellationToken ct)
    {
        var userId = AccessService.UserId(User);
        var removed = await db.Subscriptions.Where(s => s.UserId == userId && s.BookId == id).ExecuteDeleteAsync(ct);
        if (removed > 0) await log.WriteAsync(ActivityTypes.Unsubscribed, userId, id, ct: ct);
        return RedirectToAction(nameof(Details), new { id });
    }

    private bool CanEdit(Book book) =>
        AccessService.ReadsEverything(User) || book.UploadedById == AccessService.UserId(User);
}
