using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Obrl.Web.Data;
using Obrl.Web.Domain;
using Obrl.Web.Models;
using Obrl.Web.Services;
using Obrl.Web.Services.Ingest;
using Obrl.Web.Services.Rendering;

namespace Obrl.Web.Controllers;

/// <summary>
/// The built-in reader. Page-mode books are sent one watermarked image at a time through short-lived signed URLs;
/// text-mode books are sent one cleaned HTML section at a time. The original file is never sent.
/// </summary>
[Route("read/{id:guid}")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public partial class ReaderController(
    ObrlDbContext db,
    AccessService access,
    BookStorage storage,
    PageRenderer renderer,
    UrlSigner signer,
    ActivityLog log,
    IOptions<ObrlOptions> options) : Controller
{
    private const int MaxPagesPerRequest = 6;

    [HttpGet("")]
    public async Task<IActionResult> Read(Guid id, int? at, CancellationToken ct)
    {
        var userId = AccessService.UserId(User);
        var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id && b.Status == BookStatus.Ready, ct);
        if (book is null) return NotFound();
        if (!await access.CanReadAsync(User, id, ct))
        {
            await log.WriteAsync(ActivityTypes.AccessDenied, userId, id, ct: ct);
            TempData["Message"] = "Subscribe to this book or one of its categories to read it.";
            return RedirectToAction("Details", "Books", new { id });
        }

        var progress = await db.ReadingProgress.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId && p.BookId == id, ct);
        await log.WriteAsync(ActivityTypes.BookOpened, userId, id, ct: ct);

        var position = Math.Clamp(at ?? progress?.Position ?? 0, 0, Math.Max(0, book.UnitCount - 1));
        return View(new ReaderVm
        {
            Book = book,
            Position = position,
            Offset = at is null ? progress?.Offset ?? 0 : 0,
            Bookmarks = await db.Bookmarks.AsNoTracking().Where(m => m.UserId == userId && m.BookId == id)
                .OrderBy(m => m.Position).ThenBy(m => m.Offset).ToListAsync(ct),
            TocJson = book.TocJson ?? "[]",
            UserLabel = User.Identity?.Name ?? "",
            UserIdShort = userId[..Math.Min(8, userId.Length)],
        });
    }

    // ---------- Page mode ----------

    /// <summary>Signed, short-lived URLs for a few pages. The reader asks for more as the user turns pages.</summary>
    [HttpGet("pages")]
    public async Task<IActionResult> PageUrls(Guid id, int start, int count = 3, int w = 1200, CancellationToken ct = default)
    {
        var book = await ReadableBookAsync(id, ReaderMode.Page, ct);
        if (book is null) return NotFound();

        var userId = AccessService.UserId(User);
        var width = renderer.SnapWidth(w);
        var lifetime = TimeSpan.FromSeconds(options.Value.PageUrlLifetimeSeconds);
        var first = Math.Clamp(start, 0, book.UnitCount - 1);
        var last = Math.Min(book.UnitCount - 1, first + Math.Clamp(count, 1, MaxPagesPerRequest) - 1);

        var items = Enumerable.Range(first, last - first + 1).Select(n =>
        {
            var (exp, sig) = signer.Sign(userId, id, $"page:{n}:{width}", lifetime);
            return new { n, url = $"/read/{id}/page/{n}?w={width}&e={exp}&s={sig}" };
        });
        return Json(new { width, items });
    }

    [HttpGet("page/{n:int}")]
    [EnableRateLimiting("reader")]
    public async Task<IActionResult> Page(Guid id, int n, int w, long e, string? s, CancellationToken ct)
    {
        var userId = AccessService.UserId(User);
        if (!signer.Verify(userId, id, $"page:{n}:{w}", e, s)) return StatusCode(StatusCodes.Status410Gone);
        var book = await ReadableBookAsync(id, ReaderMode.Page, ct);
        if (book is null || n < 0 || n >= book.UnitCount) return NotFound();

        var image = await renderer.RenderAsync(id, n, w, new Watermark(User.Identity?.Name ?? userId, userId), ct);
        return File(image, "image/webp");
    }

    // ---------- Text mode ----------

    [HttpGet("section/{n:int}")]
    [EnableRateLimiting("reader")]
    public async Task<IActionResult> Section(Guid id, int n, CancellationToken ct)
    {
        var book = await ReadableBookAsync(id, ReaderMode.Text, ct);
        if (book is null || n < 0 || n >= book.UnitCount) return NotFound();

        var html = Encoding.UTF8.GetString(await storage.ReadDecryptedAsync(storage.SectionPath(id, n), ct));
        var userId = AccessService.UserId(User);
        var lifetime = TimeSpan.FromSeconds(options.Value.PageUrlLifetimeSeconds);
        var resources = ResourceRegex().Matches(html)
            .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .Distinct()
            .ToDictionary(r => r.ToString(CultureInfo.InvariantCulture), r =>
            {
                var (exp, sig) = signer.Sign(userId, id, $"res:{r}", lifetime);
                return $"/read/{id}/res/{r}?e={exp}&s={sig}";
            });
        return Json(new { n, count = book.UnitCount, html, resources });
    }

    [HttpGet("res/{n:int}")]
    public async Task<IActionResult> Resource(Guid id, int n, long e, string? s, CancellationToken ct)
    {
        var userId = AccessService.UserId(User);
        if (!signer.Verify(userId, id, $"res:{n}", e, s)) return StatusCode(StatusCodes.Status410Gone);
        var book = await ReadableBookAsync(id, ReaderMode.Text, ct);
        if (book is null) return NotFound();
        var types = await storage.GetResourceTypesAsync(id, ct);
        if (n < 0 || n >= types.Count) return NotFound();
        var type = types[n].StartsWith("image/", StringComparison.OrdinalIgnoreCase) ? types[n] : "application/octet-stream";
        return File(await storage.ReadDecryptedAsync(storage.ResourcePath(id, n), ct), type);
    }

    [GeneratedRegex("""data-obrl-res="(\d+)""")]
    private static partial Regex ResourceRegex();

    // ---------- Progress and bookmarks ----------

    public sealed record ProgressDto(int Position, double Offset, double Percent);

    [HttpPost("progress"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveProgress(Guid id, [FromBody] ProgressDto dto, CancellationToken ct)
    {
        var book = await ReadableBookAsync(id, null, ct);
        if (book is null) return NotFound();
        var userId = AccessService.UserId(User);
        var position = Math.Clamp(dto.Position, 0, Math.Max(0, book.UnitCount - 1));
        var offset = Math.Clamp(dto.Offset, 0, 1);
        var percent = Math.Clamp(dto.Percent, 0, 100);
        var now = DateTimeOffset.UtcNow;

        // One statement, so two tabs saving at the same moment cannot collide.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ReadingProgress" ("UserId", "BookId", "Position", "Offset", "PercentComplete", "UpdatedAt")
            VALUES ({userId}, {id}, {position}, {offset}, {percent}, {now})
            ON CONFLICT ("UserId", "BookId") DO UPDATE SET
                "Position" = EXCLUDED."Position", "Offset" = EXCLUDED."Offset",
                "PercentComplete" = EXCLUDED."PercentComplete", "UpdatedAt" = EXCLUDED."UpdatedAt"
            """, ct);
        return NoContent();
    }

    public sealed record BookmarkDto(int Position, double Offset, string? Note);

    [HttpPost("bookmarks"), ValidateAntiForgeryToken]
    public async Task<IActionResult> AddBookmark(Guid id, [FromBody] BookmarkDto dto, CancellationToken ct)
    {
        var book = await ReadableBookAsync(id, null, ct);
        if (book is null) return NotFound();
        var userId = AccessService.UserId(User);
        var mark = new Bookmark
        {
            UserId = userId,
            BookId = id,
            Position = Math.Clamp(dto.Position, 0, Math.Max(0, book.UnitCount - 1)),
            Offset = Math.Clamp(dto.Offset, 0, 1),
            Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim()[..Math.Min(500, dto.Note.Trim().Length)],
        };
        db.Bookmarks.Add(mark);
        log.Add(ActivityTypes.BookmarkAdded, userId, id, new { mark.Position });
        await db.SaveChangesAsync(ct);
        return Json(new { mark.Id, mark.Position, mark.Offset, mark.Note });
    }

    [HttpDelete("bookmarks/{bookmarkId:long}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteBookmark(Guid id, long bookmarkId, CancellationToken ct)
    {
        var userId = AccessService.UserId(User);
        await db.Bookmarks.Where(m => m.Id == bookmarkId && m.UserId == userId && m.BookId == id).ExecuteDeleteAsync(ct);
        return NoContent();
    }

    private async Task<Book?> ReadableBookAsync(Guid id, ReaderMode? mode, CancellationToken ct)
    {
        var query = access.ReadableBooks(User).AsNoTracking().Where(b => b.Id == id);
        if (mode is not null) query = query.Where(b => b.Mode == mode);
        return await query.FirstOrDefaultAsync(ct);
    }
}
