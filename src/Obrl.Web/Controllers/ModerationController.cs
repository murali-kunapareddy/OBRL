using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Obrl.Web.Data;
using Obrl.Web.Domain;
using Obrl.Web.Models;
using Obrl.Web.Services;

namespace Obrl.Web.Controllers;

/// <summary>The Moderator's review queue: books waiting for approval and reported comments.</summary>
[Authorize(Policy = Policies.CanModerate)]
[Route("moderation")]
public class ModerationController(ObrlDbContext db, ActivityLog log) : Controller
{
    /// <summary>Books waiting for a decision (not yet approved, not rejected).</summary>
    public static IQueryable<Book> Pending(ObrlDbContext db) =>
        db.Books.Where(b => !b.IsApproved && b.RejectedAt == null && b.Status != BookStatus.Failed);

    /// <summary>Everything a Moderator still has to look at; shown as a badge in the menu.</summary>
    public static async Task<int> WaitingCountAsync(ObrlDbContext db, CancellationToken ct = default) =>
        await Pending(db).CountAsync(ct)
        + await db.Comments.CountAsync(c => c.Reports.Any(r => r.ResolvedAt == null), ct);

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var books = await Pending(db).AsNoTracking()
            .OrderBy(b => b.CreatedAt).Take(200)
            .Select(b => new PendingBookRow(b.Id, b.Title, b.Author, b.OriginalFormat, b.Status,
                b.UploadedBy!.DisplayName, b.CreatedAt, b.HasCover))
            .ToListAsync(ct);

        var reported = await db.Comments.AsNoTracking()
            .Where(c => c.Reports.Any(r => r.ResolvedAt == null))
            .OrderBy(c => c.Reports.Where(r => r.ResolvedAt == null).Min(r => r.CreatedAt)).Take(200)
            .Select(c => new ReportedCommentRow(c.Id, c.BookId, c.Book!.Title, c.User!.DisplayName, c.Body, c.IsHidden, c.CreatedAt,
                c.Reports.Where(r => r.ResolvedAt == null).OrderBy(r => r.CreatedAt).Select(r => r.Reason ?? "(no reason given)").ToList()))
            .ToListAsync(ct);

        return View(new ModerationVm { PendingBooks = books, ReportedComments = reported });
    }

    [HttpPost("books/{id:guid}/approve"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        var book = await db.Books.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (book is null) return NotFound();
        var userId = AccessService.UserId(User);
        book.IsApproved = true;
        book.ApprovedById = userId;
        book.ApprovedAt = DateTimeOffset.UtcNow;
        book.RejectedAt = null;
        book.ReviewNote = null;
        log.Add(ActivityTypes.BookApproved, userId, id);
        await db.SaveChangesAsync(ct);
        TempData["Message"] = $"\"{book.Title}\" is now in the library.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("books/{id:guid}/reject"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(Guid id, string? note, CancellationToken ct)
    {
        var book = await db.Books.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (book is null) return NotFound();
        var userId = AccessService.UserId(User);
        book.IsApproved = false;
        book.ApprovedById = null;
        book.ApprovedAt = null;
        book.RejectedAt = DateTimeOffset.UtcNow;
        book.ReviewNote = Community.CleanText(note, 1000);
        log.Add(ActivityTypes.BookRejected, userId, id, new { note = book.ReviewNote });
        await db.SaveChangesAsync(ct);
        TempData["Message"] = $"\"{book.Title}\" was sent back to the uploader.";
        return RedirectToAction(nameof(Index));
    }
}
