using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Obrl.Web.Data;
using Obrl.Web.Domain;
using Obrl.Web.Services;

namespace Obrl.Web.Controllers;

/// <summary>
/// Comments on books. Readers of a book can comment and reply (one level deep),
/// delete their own comments and report others. Moderators hide and restore comments.
/// </summary>
public class CommentsController(ObrlDbContext db, AccessService access, ActivityLog log) : Controller
{
    [HttpPost("/books/{bookId:guid}/comments"), ValidateAntiForgeryToken, EnableRateLimiting("posting")]
    public async Task<IActionResult> Create(Guid bookId, string? body, long? parentId, CancellationToken ct)
    {
        if (!await access.CanReadAsync(User, bookId, ct)) return NotFound();
        var text = Community.CleanText(body, Community.MaxCommentLength);
        if (text is null)
        {
            TempData["Message"] = "Write something before posting.";
            return Redirect($"/books/{bookId}#comments");
        }

        if (parentId is long pid)
        {
            var parent = await db.Comments.AsNoTracking().FirstOrDefaultAsync(c => c.Id == pid && c.BookId == bookId, ct);
            if (parent is null) return NotFound();
            // Replies stay one level deep: answering a reply joins the same thread.
            parentId = parent.ParentId ?? parent.Id;
        }

        var userId = AccessService.UserId(User);
        var comment = new Comment { BookId = bookId, UserId = userId, ParentId = parentId, Body = text };
        db.Comments.Add(comment);
        log.Add(ActivityTypes.Commented, userId, bookId, new { reply = parentId is not null });
        await db.SaveChangesAsync(ct);
        return Redirect($"/books/{bookId}#comment-{comment.Id}");
    }

    [HttpPost("/comments/{id:long}/delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var userId = AccessService.UserId(User);
        var comment = await db.Comments.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (comment is null) return NotFound();
        if (comment.UserId != userId) return Forbid();

        // Keep the thread readable: a comment with replies becomes a "removed" placeholder.
        if (await db.Comments.AnyAsync(c => c.ParentId == id, ct))
        {
            comment.IsHidden = true;
            comment.HiddenById = userId;
            comment.HiddenReason = Community.RemovedByAuthor;
        }
        else
        {
            db.Comments.Remove(comment);
        }
        log.Add(ActivityTypes.CommentDeleted, userId, comment.BookId, new { commentId = id });
        await db.SaveChangesAsync(ct);
        TempData["Message"] = "Your comment was deleted.";
        return Redirect($"/books/{comment.BookId}#comments");
    }

    [HttpPost("/comments/{id:long}/report"), ValidateAntiForgeryToken, EnableRateLimiting("posting")]
    public async Task<IActionResult> Report(long id, string? reason, CancellationToken ct)
    {
        var userId = AccessService.UserId(User);
        var comment = await db.Comments.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (comment is null || !await access.CanReadAsync(User, comment.BookId, ct)) return NotFound();
        if (comment.UserId == userId) return BadRequest();

        if (!await db.CommentReports.AnyAsync(r => r.CommentId == id && r.UserId == userId, ct))
        {
            db.CommentReports.Add(new CommentReport { CommentId = id, UserId = userId, Reason = Community.CleanText(reason, 500) });
            log.Add(ActivityTypes.CommentReported, userId, comment.BookId, new { commentId = id });
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Reported twice at once; one report is kept.
            }
        }
        TempData["Message"] = "Thanks. A Moderator will take a look.";
        return Redirect($"/books/{comment.BookId}#comment-{id}");
    }

    [HttpPost("/comments/{id:long}/hide"), ValidateAntiForgeryToken, Authorize(Policy = Policies.CanModerate)]
    public async Task<IActionResult> Hide(long id, string? reason, string? returnUrl, CancellationToken ct)
    {
        var comment = await db.Comments.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (comment is null) return NotFound();
        var userId = AccessService.UserId(User);
        comment.IsHidden = true;
        comment.HiddenById = userId;
        comment.HiddenReason = Community.CleanText(reason, 300) ?? "Hidden by a Moderator";
        await ResolveReportsAsync(id, ct);
        log.Add(ActivityTypes.CommentHidden, userId, comment.BookId, new { commentId = id, hidden = true });
        await db.SaveChangesAsync(ct);
        return Back(returnUrl, comment.BookId, id);
    }

    [HttpPost("/comments/{id:long}/unhide"), ValidateAntiForgeryToken, Authorize(Policy = Policies.CanModerate)]
    public async Task<IActionResult> Unhide(long id, string? returnUrl, CancellationToken ct)
    {
        var comment = await db.Comments.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (comment is null) return NotFound();
        // A comment its author deleted stays deleted.
        if (comment.HiddenById == comment.UserId) return BadRequest();
        comment.IsHidden = false;
        comment.HiddenById = null;
        comment.HiddenReason = null;
        log.Add(ActivityTypes.CommentHidden, AccessService.UserId(User), comment.BookId, new { commentId = id, hidden = false });
        await db.SaveChangesAsync(ct);
        return Back(returnUrl, comment.BookId, id);
    }

    /// <summary>Moderator looked at the reports and decided the comment can stay.</summary>
    [HttpPost("/comments/{id:long}/keep"), ValidateAntiForgeryToken, Authorize(Policy = Policies.CanModerate)]
    public async Task<IActionResult> Keep(long id, string? returnUrl, CancellationToken ct)
    {
        var comment = await db.Comments.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (comment is null) return NotFound();
        await ResolveReportsAsync(id, ct);
        log.Add(ActivityTypes.CommentHidden, AccessService.UserId(User), comment.BookId, new { commentId = id, kept = true });
        await db.SaveChangesAsync(ct);
        return Back(returnUrl, comment.BookId, id);
    }

    private async Task ResolveReportsAsync(long commentId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var r in await db.CommentReports.Where(r => r.CommentId == commentId && r.ResolvedAt == null).ToListAsync(ct))
            r.ResolvedAt = now;
    }

    private IActionResult Back(string? returnUrl, Guid bookId, long commentId) =>
        Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : Redirect($"/books/{bookId}#comment-{commentId}");
}
