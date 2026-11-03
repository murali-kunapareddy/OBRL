using Microsoft.Extensions.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Obrl.Web.Data;
using Obrl.Web.Domain;
using Obrl.Web.Models;
using Obrl.Web.Services;

namespace Obrl.Web.Controllers;

/// <summary>
/// The message board: every member can start a topic and reply.
/// Moderators pin, lock and hide topics and replies.
/// </summary>
[Route("board")]
public class BoardController(ObrlDbContext db, ActivityLog log, IStringLocalizer<SharedResource> L) : Controller
{
    private const int PageSize = 30;

    /// <summary>Topics a viewer may see, as list rows. Shared with the dashboard.</summary>
    public static IQueryable<BoardTopicRow> TopicRows(ObrlDbContext db, bool isModerator, long? onlyId = null) =>
        db.BoardPosts.AsNoTracking()
            .Where(p => p.ParentId == null && (isModerator || !p.IsHidden) && (onlyId == null || p.Id == onlyId))
            .OrderByDescending(p => p.IsPinned).ThenByDescending(p => p.LastActivityAt)
            .Select(p => new BoardTopicRow(p.Id, p.Title ?? "", p.User!.DisplayName, p.CreatedAt, p.LastActivityAt,
                p.Replies.Count(r => isModerator || !r.IsHidden), p.IsPinned, p.IsLocked, p.IsHidden));

    [HttpGet("")]
    public async Task<IActionResult> Index(int page = 1, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        var isModerator = AccessService.ReadsEverything(User);
        var rows = await TopicRows(db, isModerator).Skip((page - 1) * PageSize).Take(PageSize + 1).ToListAsync(ct);
        return View(new BoardIndexVm
        {
            Topics = rows.Take(PageSize).ToList(),
            Page = page,
            HasMore = rows.Count > PageSize,
            CanModerate = isModerator,
        });
    }

    [HttpGet("new")]
    public IActionResult New() => View(new NewTopicVm());

    [HttpPost("new"), ValidateAntiForgeryToken, EnableRateLimiting("posting")]
    public async Task<IActionResult> New(NewTopicVm vm, CancellationToken ct)
    {
        var title = Community.CleanText(vm.Title?.Replace('\n', ' '), Community.MaxTitleLength);
        var body = Community.CleanText(vm.Body, Community.MaxPostLength);
        if (title is null) ModelState.AddModelError(nameof(vm.Title), L["Give the topic a title."]);
        if (body is null) ModelState.AddModelError(nameof(vm.Body), L["Write a message."]);
        if (!ModelState.IsValid) return View(vm);

        var userId = AccessService.UserId(User);
        var post = new BoardPost { UserId = userId, Title = title, Body = body! };
        db.BoardPosts.Add(post);
        log.Add(ActivityTypes.BoardPosted, userId, data: new { topic = true });
        await db.SaveChangesAsync(ct);
        return Redirect($"/board/{post.Id}");
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Topic(long id, CancellationToken ct)
    {
        var isModerator = AccessService.ReadsEverything(User);
        var userId = AccessService.UserId(User);
        var topic = await TopicRows(db, isModerator, id).FirstOrDefaultAsync(ct);
        if (topic is null) return NotFound();

        var posts = await db.BoardPosts.AsNoTracking()
            .Where(p => p.Id == id || (p.ParentId == id && (isModerator || !p.IsHidden)))
            .OrderBy(p => p.CreatedAt)
            .Select(p => new { p.Id, View = new BoardPostView(p.Id, p.User!.DisplayName, p.Body, p.CreatedAt, p.IsHidden, p.UserId == userId) })
            .ToListAsync(ct);

        return View(new BoardTopicVm
        {
            Topic = topic,
            Opening = posts.First(p => p.Id == id).View,
            Replies = posts.Where(p => p.Id != id).Select(p => p.View).ToList(),
            CanModerate = isModerator,
            CanReply = !topic.IsLocked || isModerator,
        });
    }

    [HttpPost("{id:long}/reply"), ValidateAntiForgeryToken, EnableRateLimiting("posting")]
    public async Task<IActionResult> Reply(long id, string? body, CancellationToken ct)
    {
        var isModerator = AccessService.ReadsEverything(User);
        var topic = await db.BoardPosts.FirstOrDefaultAsync(p => p.Id == id && p.ParentId == null, ct);
        if (topic is null || (topic.IsHidden && !isModerator)) return NotFound();
        if (topic.IsLocked && !isModerator)
        {
            TempData["Message"] = L["This topic is locked."].Value;
            return Redirect($"/board/{id}");
        }
        var text = Community.CleanText(body, Community.MaxPostLength);
        if (text is null)
        {
            TempData["Message"] = L["Write something before posting."].Value;
            return Redirect($"/board/{id}#reply");
        }

        var userId = AccessService.UserId(User);
        var reply = new BoardPost { ParentId = id, UserId = userId, Body = text };
        db.BoardPosts.Add(reply);
        topic.LastActivityAt = reply.CreatedAt;
        log.Add(ActivityTypes.BoardPosted, userId, data: new { topicId = id });
        await db.SaveChangesAsync(ct);
        return Redirect($"/board/{id}#post-{reply.Id}");
    }

    [HttpPost("post/{id:long}/delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var userId = AccessService.UserId(User);
        var post = await db.BoardPosts.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (post is null) return NotFound();
        if (post.UserId != userId) return Forbid();

        var topicId = post.ParentId;
        // A topic others have answered is hidden rather than deleted, so their replies are not lost silently.
        if (post.ParentId is null && await db.BoardPosts.AnyAsync(p => p.ParentId == id && p.UserId != userId, ct))
            post.IsHidden = true;
        else
            db.BoardPosts.Remove(post);
        await db.SaveChangesAsync(ct);
        TempData["Message"] = L["Your post was deleted."].Value;
        return topicId is long t ? Redirect($"/board/{t}") : Redirect("/board");
    }

    [HttpPost("post/{id:long}/moderate"), ValidateAntiForgeryToken, Authorize(Policy = Policies.CanModerate)]
    public async Task<IActionResult> Moderate(long id, string op, CancellationToken ct)
    {
        var post = await db.BoardPosts.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (post is null) return NotFound();
        var isTopic = post.ParentId is null;
        switch (op)
        {
            case "hide": post.IsHidden = true; break;
            case "unhide": post.IsHidden = false; break;
            case "pin" when isTopic: post.IsPinned = true; break;
            case "unpin" when isTopic: post.IsPinned = false; break;
            case "lock" when isTopic: post.IsLocked = true; break;
            case "unlock" when isTopic: post.IsLocked = false; break;
            default: return BadRequest();
        }
        log.Add(ActivityTypes.BoardModerated, AccessService.UserId(User), data: new { postId = id, action = op });
        await db.SaveChangesAsync(ct);
        return Redirect(isTopic ? $"/board/{id}" : $"/board/{post.ParentId}#post-{id}");
    }
}
