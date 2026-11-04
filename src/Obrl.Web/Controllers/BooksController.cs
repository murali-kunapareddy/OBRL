using Microsoft.Extensions.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Obrl.Web.Data;
using Obrl.Web.Domain;
using Obrl.Web.Models;
using Obrl.Web.Services;

namespace Obrl.Web.Controllers;

public class BooksController(ObrlDbContext db, AccessService access, BookText bookText, BookStorage storage, ActivityLog log, IStringLocalizer<SharedResource> L) : Controller
{
    [HttpGet("/books/{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var userId = AccessService.UserId(User);
        var book = await db.Books.AsNoTracking().Include(b => b.Categories).FirstOrDefaultAsync(b => b.Id == id, ct);
        if (book is null) return NotFound();
        var canEdit = CanEdit(book);
        if ((book.Status != BookStatus.Ready || !book.IsApproved) && !canEdit) return NotFound();

        var tree = await CategoryTree.LoadAsync(db, ct);
        var isModerator = AccessService.ReadsEverything(User);
        var commentRows = await db.Comments.AsNoTracking()
            .Where(c => c.BookId == id)
            .OrderByDescending(c => c.CreatedAt).Take(500)
            .Select(c => new CommentRow(c.Id, c.ParentId, c.UserId, c.User!.DisplayName, c.Body, c.CreatedAt, c.IsHidden, c.HiddenReason,
                c.Reports.Count(r => r.ResolvedAt == null), c.Reports.Any(r => r.UserId == userId)))
            .ToListAsync(ct);

        return View(new BookDetailsVm
        {
            Book = book,
            Text = await bookText.ForBookAsync(book, ct),
            TranslatedLanguages = canEdit
                ? await db.BookTranslations.Where(t => t.BookId == id).Select(t => t.Language).ToListAsync(ct)
                : [],
            CategoryTrails = book.Categories.Select(c => tree.Trail(c.CategoryId)).Where(t => t.Count > 0).ToList(),
            CanRead = book.Status == BookStatus.Ready && await access.CanReadAsync(User, id, ct),
            SubscribedToBook = await db.Subscriptions.AnyAsync(s => s.UserId == userId && s.BookId == id, ct),
            CanEdit = canEdit,
            Progress = await db.ReadingProgress.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId && p.BookId == id, ct),
            Bookmarks = await db.Bookmarks.AsNoTracking().Where(m => m.UserId == userId && m.BookId == id)
                .OrderBy(m => m.Position).ThenBy(m => m.Offset).ToListAsync(ct),
            MyRating = await db.Ratings.Where(r => r.UserId == userId && r.BookId == id).Select(r => (int?)r.Stars).FirstOrDefaultAsync(ct),
            Comments = Community.BuildThreads(commentRows, userId, isModerator),
            CanModerate = isModerator,
        });
    }

    /// <summary>Rate a book 1 to 5 stars, or 0 to take the rating back. Only readers of the book can rate it.</summary>
    [HttpPost("/books/{id:guid}/rate"), ValidateAntiForgeryToken, EnableRateLimiting("posting")]
    public async Task<IActionResult> Rate(Guid id, int stars, CancellationToken ct)
    {
        if (stars is < 0 or > 5) return BadRequest();
        if (!await access.CanReadAsync(User, id, ct)) return NotFound();
        var userId = AccessService.UserId(User);

        if (stars == 0)
        {
            await db.Ratings.Where(r => r.UserId == userId && r.BookId == id).ExecuteDeleteAsync(ct);
        }
        else
        {
            // Insert or change in one statement, so a double click cannot create a duplicate.
            var now = DateTimeOffset.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Ratings" ("UserId", "BookId", "Stars", "UpdatedAt") VALUES ({userId}, {id}, {stars}, {now})
                ON CONFLICT ("UserId", "BookId") DO UPDATE SET "Stars" = EXCLUDED."Stars", "UpdatedAt" = EXCLUDED."UpdatedAt"
                """, ct);
        }
        log.Add(ActivityTypes.Rated, userId, id, new { stars });
        await db.SaveChangesAsync(ct);

        // Recount in the database, so concurrent ratings never leave the totals out of step.
        await db.Books.Where(b => b.Id == id).ExecuteUpdateAsync(set => set
            .SetProperty(b => b.RatingCount, b => db.Ratings.Count(r => r.BookId == id))
            .SetProperty(b => b.RatingAverage, b => db.Ratings.Where(r => r.BookId == id).Average(r => (double?)r.Stars) ?? 0), ct);

        TempData["Message"] = (stars == 0 ? L["Your rating was removed."] : L["Thanks for rating this book."]).Value;
        return Redirect($"/books/{id}#rating");
    }

    /// <summary>
    /// Cover thumbnails are visible to every signed-in user, so they can browse what to subscribe to.
    /// Browsers check back each time (a cheap 304 when unchanged), so a new cover shows up straight away.
    /// </summary>
    [HttpGet("/books/{id:guid}/cover")]
    public IActionResult Cover(Guid id)
    {
        var file = new FileInfo(storage.CoverPath(id));
        if (!file.Exists) return NotFound();
        Response.Headers.CacheControl = "private, no-cache";
        var modified = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);
        var etag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{file.LastWriteTimeUtc.Ticks:x}-{file.Length:x}\"");
        return PhysicalFile(file.FullName, "image/webp", modified, etag);
    }

    [HttpPost("/books/{id:guid}/subscribe"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Subscribe(Guid id, CancellationToken ct)
    {
        var userId = AccessService.UserId(User);
        if (!await db.Books.AnyAsync(b => b.Id == id && b.Status == BookStatus.Ready && b.IsApproved, ct)) return NotFound();
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
