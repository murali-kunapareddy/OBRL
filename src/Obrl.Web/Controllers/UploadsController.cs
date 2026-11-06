using Microsoft.Extensions.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Obrl.Web.Data;
using Obrl.Web.Domain;
using Obrl.Web.Models;
using Obrl.Web.Services;
using Obrl.Web.Services.Email;
using Obrl.Web.Services.Ingest;

namespace Obrl.Web.Controllers;

/// <summary>Contributors add and edit their books; Moderators and Admins can manage every book.</summary>
[Authorize(Policy = Policies.CanContribute)]
public class UploadsController(ObrlDbContext db, BookStorage storage, CoverService covers, ActivityLog log, IOptions<ObrlOptions> options, Notifications notify, TimeProvider clock, IStringLocalizer<SharedResource> L) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var userId = AccessService.UserId(User);
        var query = db.Books.AsNoTracking().Include(b => b.UploadedBy).Include(b => b.Translations).AsQueryable();
        if (!AccessService.ReadsEverything(User)) query = query.Where(b => b.UploadedById == userId);
        ViewBag.SeesAll = AccessService.ReadsEverything(User);
        return View(await query.OrderByDescending(b => b.CreatedAt).Take(500).ToListAsync(ct));
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        ViewBag.Accept = BookFormats.Accept;
        ViewBag.MaxMb = options.Value.MaxUploadMegabytes;
        var vm = new UploadVm();
        await FillListsAsync(vm, ct);
        vm.FillTranslations([]);
        return View("Edit", vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(long.MaxValue)] // the real limit is set in Program.cs from configuration
    public async Task<IActionResult> Create(UploadVm vm, CancellationToken ct)
    {
        var format = vm.File is null ? null : BookFormats.Normalize(vm.File.FileName);
        if (vm.File is null || vm.File.Length == 0) ModelState.AddModelError(nameof(vm.File), L["Choose a file to upload."]);
        else if (format is null) ModelState.AddModelError(nameof(vm.File), L["This file type is not supported."]);
        await ValidateCategoriesAsync(vm, ct);
        await ValidateDetailsAsync(vm, ct);
        var customCover = await ReadCoverChoiceAsync(vm, hasSavedCustom: false, ct);

        if (!ModelState.IsValid)
        {
            ViewBag.Accept = BookFormats.Accept;
            ViewBag.MaxMb = options.Value.MaxUploadMegabytes;
            await FillListsAsync(vm, ct);
            vm.FillTranslations([]);
            return View("Edit", vm);
        }

        var userId = AccessService.UserId(User);
        var approvedOnUpload = AccessService.ReadsEverything(User);
        var book = new Book
        {
            IsApproved = approvedOnUpload,
            ApprovedById = approvedOnUpload ? userId : null,
            ApprovedAt = approvedOnUpload ? DateTimeOffset.UtcNow : null,
            Title = vm.Title.Trim(),
            Author = Blank(vm.Author),
            Description = Blank(vm.Description),
            LanguageId = vm.LanguageId,
            Publisher = await FindOrAddPublisherAsync(vm.PublisherName, ct),
            PublishedYear = vm.PublishedYear,
            OriginalFileName = Path.GetFileName(vm.File!.FileName),
            OriginalFormat = format!,
            OriginalSize = vm.File.Length,
            Mode = BookFormats.ModeFor(format!),
            UploadedById = userId,
            Categories = vm.CategoryIds.Distinct().Select(c => new BookCategory { CategoryId = c }).ToList(),
        };
        BookText.Apply(book, vm.Translations);
        SetCoverChoice(book, vm);
        if (customCover is not null) await covers.SaveCustomAsync(book.Id, customCover, ct);

        // The upload is encrypted before it touches the disk.
        using (var ms = new MemoryStream())
        {
            await vm.File.CopyToAsync(ms, ct);
            await storage.WriteEncryptedAsync(storage.OriginalPath(book.Id), ms.ToArray(), ct);
        }

        db.Books.Add(book);
        log.Add(ActivityTypes.BookUploaded, userId, book.Id, new { format, size = book.OriginalSize });
        await db.SaveChangesAsync(ct);
        await notify.NewUploadAsync(book, userId, ct);
        var message = approvedOnUpload
            ? L["“{0}” was uploaded and is being prepared. It usually takes under a minute.", book.Title].Value
            : L["“{0}” was uploaded. Readers will see it once a Moderator has approved it.", book.Title].Value;
        if (book.Mode == ReaderMode.Page && book.CoverSource == CoverSource.Default)
            message += " " + L["The first page is used as the cover. Once the book is ready, you can pick another page with the edit button."].Value;
        TempData["Message"] = message;
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var book = await db.Books.Include(b => b.Categories).Include(b => b.Translations).Include(b => b.Publisher).FirstOrDefaultAsync(b => b.Id == id, ct);
        if (book is null) return NotFound();
        if (!CanManage(book)) return Forbid();
        var vm = new UploadVm
        {
            Id = book.Id,
            Title = book.Title,
            Author = book.Author,
            Description = book.Description,
            CategoryIds = book.Categories.Select(c => c.CategoryId).ToList(),
            LanguageId = book.LanguageId,
            PublisherName = book.Publisher?.Name,
            PublishedYear = book.PublishedYear,
            CoverSource = book.CoverSource,
            CoverPage = book.CoverPage,
        };
        await FillListsAsync(vm, ct);
        vm.FillTranslations(book.Translations);
        FillBookInfo(vm, book);
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, UploadVm vm, CancellationToken ct)
    {
        var book = await db.Books.Include(b => b.Categories).Include(b => b.Translations).Include(b => b.Publisher).FirstOrDefaultAsync(b => b.Id == id, ct);
        if (book is null) return NotFound();
        if (!CanManage(book)) return Forbid();
        ModelState.Remove(nameof(vm.File));
        await ValidateCategoriesAsync(vm, ct);
        await ValidateDetailsAsync(vm, ct);
        if (vm.CoverSource == CoverSource.Page && book.Mode == ReaderMode.Page && book.Status == BookStatus.Ready
            && vm.CoverImage is not { Length: > 0 } && (vm.CoverPage is not int p || p < 1 || p > book.UnitCount))
            ModelState.AddModelError(nameof(vm.CoverPage), L["Pick a page between 1 and {0}.", book.UnitCount]);
        var customCover = await ReadCoverChoiceAsync(vm, covers.HasCustom(id), ct);
        var pageCrop = await ReadPageCropAsync(vm, book, ct);
        if (!ModelState.IsValid)
        {
            vm.Id = id;
            await FillListsAsync(vm, ct);
            vm.FillTranslations(book.Translations);
            FillBookInfo(vm, book);
            return View(vm);
        }

        var (oldSource, oldPage) = (book.CoverSource, book.CoverPage);
        covers.KeepDefault(book);
        SetCoverChoice(book, vm);
        if (customCover is not null) await covers.SaveCustomAsync(id, customCover, ct);
        // A cropped page belongs to one page: a new crop replaces it, and picking another page (or another kind of cover) drops it.
        var pageChanged = oldSource != book.CoverSource || oldPage != book.CoverPage;
        if (pageCrop is not null) await covers.SavePageCropAsync(id, pageCrop, ct);
        var cropDropped = pageCrop is null && (pageChanged || vm.ClearPageCrop) && covers.HasPageCrop(id);
        if (cropDropped) covers.DeletePageCrop(id);
        if (customCover is not null || pageCrop is not null || pageChanged || cropDropped)
            await covers.RefreshAsync(book, ct);

        book.Title = vm.Title.Trim();
        book.Author = Blank(vm.Author);
        book.Description = Blank(vm.Description);
        book.LanguageId = vm.LanguageId;
        book.Publisher = await FindOrAddPublisherAsync(vm.PublisherName, ct);
        book.PublishedYear = vm.PublishedYear;
        BookText.Apply(book, vm.Translations);
        var wanted = vm.CategoryIds.Distinct().ToHashSet();
        book.Categories.RemoveAll(c => !wanted.Contains(c.CategoryId));
        foreach (var c in wanted.Where(c => book.Categories.All(x => x.CategoryId != c)))
            book.Categories.Add(new BookCategory { BookId = id, CategoryId = c });

        // Editing a rejected book sends it back to the review queue.
        var resubmitted = book.RejectedAt is not null && !book.IsApproved;
        if (resubmitted) book.RejectedAt = null;
        await db.SaveChangesAsync(ct);
        TempData["Message"] = (resubmitted ? L["Saved and sent back for review."] : L["Saved."]).Value;
        return RedirectToAction("Details", "Books", new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Retry(Guid id, CancellationToken ct)
    {
        var book = await db.Books.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (book is null) return NotFound();
        if (!CanManage(book)) return Forbid();
        book.Status = BookStatus.Queued;
        book.StatusMessage = null;
        await db.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var book = await db.Books.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (book is null) return NotFound();
        if (!CanManage(book)) return Forbid();
        if (book.Status == BookStatus.Processing)
        {
            TempData["Message"] = L["This book is being converted right now. Try again in a minute."].Value;
            return RedirectToAction(nameof(Index));
        }
        db.Books.Remove(book);
        log.Add(ActivityTypes.BookDeleted, AccessService.UserId(User), id, new { book.Title });
        await db.SaveChangesAsync(ct);
        storage.DeleteAll(id);
        TempData["Message"] = L["“{0}” was deleted.", book.Title].Value;
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// How a page would look as the cover, for the page picker on the edit form. Only the uploader and
    /// Moderators can see these, and only once the book is converted.
    /// </summary>
    [HttpGet]
    [ResponseCache(Duration = 600, Location = ResponseCacheLocation.Client)]
    public async Task<IActionResult> CoverPreview(Guid id, int page, CancellationToken ct)
    {
        var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (book is null || book.Mode != ReaderMode.Page || book.Status != BookStatus.Ready) return NotFound();
        if (!CanManage(book)) return Forbid();
        if (page < 1 || page > book.UnitCount) return NotFound();
        return File(await covers.PagePreviewAsync(book, page, ct), "image/webp");
    }

    /// <summary>
    /// A whole page of a converted PDF or Word book, for cropping it into the cover on the edit form.
    /// Only the uploader and Moderators can see it. It has no watermark, like the cover itself.
    /// </summary>
    [HttpGet]
    [ResponseCache(Duration = 600, Location = ResponseCacheLocation.Client)]
    public async Task<IActionResult> CoverPageImage(Guid id, int page, CancellationToken ct)
    {
        var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (book is null || book.Mode != ReaderMode.Page || book.Status != BookStatus.Ready) return NotFound();
        if (!CanManage(book)) return Forbid();
        if (page < 1 || page > book.UnitCount) return NotFound();
        return File(await covers.WholePageAsync(book, page, ct), "image/webp");
    }

    /// <summary>
    /// The cropped and adjusted page the browser made from <see cref="CoverPageImage"/>, when the uploader used
    /// "Crop and adjust" on a page cover. Null when there is none, or it does not apply to this book.
    /// </summary>
    private async Task<byte[]?> ReadPageCropAsync(UploadVm vm, Book book, CancellationToken ct)
    {
        if (vm.PageCoverImage is not { Length: > 0 } image || vm.CoverSource != CoverSource.Page
            || book.Mode != ReaderMode.Page || vm.CoverImage is { Length: > 0 }) return null;
        if (image.Length > CoverService.MaxImageMegabytes * 1024L * 1024L)
        {
            ModelState.AddModelError(nameof(vm.CoverPage), L["The cover image must be {0} MB or smaller.", CoverService.MaxImageMegabytes]);
            return null;
        }
        var webp = await CoverService.PrepareCustomAsync(image, ct);
        if (webp is null) ModelState.AddModelError(nameof(vm.CoverPage), L["The cover image could not be read. Use a JPEG, PNG or WebP picture."]);
        return webp;
    }

    /// <summary>
    /// Checks the cover part of the form. An uploaded image always wins; returns it ready to save (or null when none
    /// was uploaded). Adds model errors for an unreadable image, or for "use my image" with no image.
    /// </summary>
    private async Task<byte[]?> ReadCoverChoiceAsync(UploadVm vm, bool hasSavedCustom, CancellationToken ct)
    {
        if (vm.CoverImage is { Length: > 0 } image)
        {
            if (image.Length > CoverService.MaxImageMegabytes * 1024L * 1024L)
            {
                ModelState.AddModelError(nameof(vm.CoverImage), L["The cover image must be {0} MB or smaller.", CoverService.MaxImageMegabytes]);
                return null;
            }
            var webp = await CoverService.PrepareCustomAsync(image, ct);
            if (webp is null) ModelState.AddModelError(nameof(vm.CoverImage), L["The cover image could not be read. Use a JPEG, PNG or WebP picture."]);
            else vm.CoverSource = CoverSource.Custom;
            return webp;
        }
        if (vm.CoverSource == CoverSource.Custom && !hasSavedCustom)
            ModelState.AddModelError(nameof(vm.CoverImage), L["Choose a cover image, or pick another cover option."]);
        return null;
    }

    /// <summary>Copies the cover choice from the form. A page only applies to PDF and Word books.</summary>
    private static void SetCoverChoice(Book book, UploadVm vm)
    {
        var source = vm.CoverSource;
        if (!Enum.IsDefined(source) || (source == CoverSource.Page && (book.Mode != ReaderMode.Page || vm.CoverPage is null)))
            source = CoverSource.Default;
        book.CoverSource = source;
        book.CoverPage = source == CoverSource.Page ? vm.CoverPage : null;
    }

    private void FillBookInfo(UploadVm vm, Book book)
    {
        vm.Status = book.Status;
        vm.Mode = book.Mode;
        vm.PageCount = book.Mode == ReaderMode.Page ? book.UnitCount : 0;
        vm.HasCover = book.HasCover;
        vm.HasCustomCover = covers.HasCustom(book.Id);
        vm.HasPageCrop = book.CoverSource == CoverSource.Page && covers.HasPageCrop(book.Id);
    }

    private bool CanManage(Book book) =>
        AccessService.ReadsEverything(User) || book.UploadedById == AccessService.UserId(User);

    private async Task ValidateCategoriesAsync(UploadVm vm, CancellationToken ct)
    {
        if (vm.CategoryIds.Count == 0)
        {
            ModelState.AddModelError(nameof(vm.CategoryIds), L["Pick at least one category, so readers can find the book."]);
            return;
        }
        var ids = vm.CategoryIds.Distinct().ToList();
        if (await db.Categories.CountAsync(c => ids.Contains(c.Id), ct) != ids.Count)
            ModelState.AddModelError(nameof(vm.CategoryIds), L["One of the categories no longer exists."]);
    }

    /// <summary>The language must still be on the list; the year has four digits and is not in the future.</summary>
    private async Task ValidateDetailsAsync(UploadVm vm, CancellationToken ct)
    {
        if (vm.LanguageId is int languageId && !await db.BookLanguages.AnyAsync(l => l.Id == languageId, ct))
            ModelState.AddModelError(nameof(vm.LanguageId), L["That language is no longer on the list. Pick another one."]);
        var thisYear = clock.GetUtcNow().Year;
        if (vm.PublishedYear is int year && !IsValidYear(year, thisYear))
            ModelState.AddModelError(nameof(vm.PublishedYear), L["Enter a four-digit year from {0} to {1}.", MinYear, thisYear]);
    }

    public const int MinYear = 1000;

    public static bool IsValidYear(int year, int thisYear) => year >= MinYear && year <= thisYear;

    /// <summary>
    /// The publisher with this name (ignoring case and extra spaces), or a new one added to the list.
    /// Null when the field was left empty.
    /// </summary>
    private async Task<Publisher?> FindOrAddPublisherAsync(string? name, CancellationToken ct)
    {
        var clean = string.Join(' ', (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length == 0) return null;
        var normalized = Publisher.Normalize(clean);
        var existing = await db.Publishers.FirstOrDefaultAsync(p => p.NormalizedName == normalized, ct);
        if (existing is not null) return existing;
        var added = new Publisher { Name = clean, NormalizedName = normalized };
        db.Publishers.Add(added);
        return added;
    }

    private async Task FillListsAsync(UploadVm vm, CancellationToken ct)
    {
        vm.AllCategories = (await CategoryTree.LoadAsync(db, ct)).Flatten();
        vm.AllLanguages = await db.BookLanguages.AsNoTracking().OrderBy(l => l.SortOrder).ThenBy(l => l.Name).ToListAsync(ct);
        vm.AllPublishers = await db.Publishers.AsNoTracking().OrderBy(p => p.Name).Select(p => p.Name).Take(5000).ToListAsync(ct);
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
