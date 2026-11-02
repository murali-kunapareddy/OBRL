using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Obrl.Web.Data;
using Obrl.Web.Domain;
using Obrl.Web.Models;
using Obrl.Web.Services;
using Obrl.Web.Services.Ingest;

namespace Obrl.Web.Controllers;

/// <summary>Contributors add and edit their books; Moderators and Admins can manage every book.</summary>
[Authorize(Policy = Policies.CanContribute)]
public class UploadsController(ObrlDbContext db, BookStorage storage, ActivityLog log, IOptions<ObrlOptions> options) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var userId = AccessService.UserId(User);
        var query = db.Books.AsNoTracking().Include(b => b.UploadedBy).AsQueryable();
        if (!AccessService.ReadsEverything(User)) query = query.Where(b => b.UploadedById == userId);
        ViewBag.SeesAll = AccessService.ReadsEverything(User);
        return View(await query.OrderByDescending(b => b.CreatedAt).Take(500).ToListAsync(ct));
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        ViewBag.Accept = BookFormats.Accept;
        ViewBag.MaxMb = options.Value.MaxUploadMegabytes;
        return View("Edit", new UploadVm { AllCategories = (await CategoryTree.LoadAsync(db, ct)).Flatten() });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(long.MaxValue)] // the real limit is set in Program.cs from configuration
    public async Task<IActionResult> Create(UploadVm vm, CancellationToken ct)
    {
        var format = vm.File is null ? null : BookFormats.Normalize(vm.File.FileName);
        if (vm.File is null || vm.File.Length == 0) ModelState.AddModelError(nameof(vm.File), "Choose a file to upload.");
        else if (format is null) ModelState.AddModelError(nameof(vm.File), "This file type is not supported.");
        await ValidateCategoriesAsync(vm, ct);

        if (!ModelState.IsValid)
        {
            ViewBag.Accept = BookFormats.Accept;
            ViewBag.MaxMb = options.Value.MaxUploadMegabytes;
            vm.AllCategories = (await CategoryTree.LoadAsync(db, ct)).Flatten();
            return View("Edit", vm);
        }

        var userId = AccessService.UserId(User);
        var book = new Book
        {
            Title = vm.Title.Trim(),
            Author = Blank(vm.Author),
            Description = Blank(vm.Description),
            OriginalFileName = Path.GetFileName(vm.File!.FileName),
            OriginalFormat = format!,
            OriginalSize = vm.File.Length,
            Mode = BookFormats.ModeFor(format!),
            UploadedById = userId,
            Categories = vm.CategoryIds.Distinct().Select(c => new BookCategory { CategoryId = c }).ToList(),
        };

        // The upload is encrypted before it touches the disk.
        using (var ms = new MemoryStream())
        {
            await vm.File.CopyToAsync(ms, ct);
            await storage.WriteEncryptedAsync(storage.OriginalPath(book.Id), ms.ToArray(), ct);
        }

        db.Books.Add(book);
        log.Add(ActivityTypes.BookUploaded, userId, book.Id, new { format, size = book.OriginalSize });
        await db.SaveChangesAsync(ct);
        TempData["Message"] = $"\"{book.Title}\" was uploaded and is being prepared. It usually takes under a minute.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var book = await db.Books.Include(b => b.Categories).FirstOrDefaultAsync(b => b.Id == id, ct);
        if (book is null) return NotFound();
        if (!CanManage(book)) return Forbid();
        return View(new UploadVm
        {
            Id = book.Id,
            Title = book.Title,
            Author = book.Author,
            Description = book.Description,
            CategoryIds = book.Categories.Select(c => c.CategoryId).ToList(),
            AllCategories = (await CategoryTree.LoadAsync(db, ct)).Flatten(),
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, UploadVm vm, CancellationToken ct)
    {
        var book = await db.Books.Include(b => b.Categories).FirstOrDefaultAsync(b => b.Id == id, ct);
        if (book is null) return NotFound();
        if (!CanManage(book)) return Forbid();
        ModelState.Remove(nameof(vm.File));
        await ValidateCategoriesAsync(vm, ct);
        if (!ModelState.IsValid)
        {
            vm.Id = id;
            vm.AllCategories = (await CategoryTree.LoadAsync(db, ct)).Flatten();
            return View(vm);
        }

        book.Title = vm.Title.Trim();
        book.Author = Blank(vm.Author);
        book.Description = Blank(vm.Description);
        var wanted = vm.CategoryIds.Distinct().ToHashSet();
        book.Categories.RemoveAll(c => !wanted.Contains(c.CategoryId));
        foreach (var c in wanted.Where(c => book.Categories.All(x => x.CategoryId != c)))
            book.Categories.Add(new BookCategory { BookId = id, CategoryId = c });
        await db.SaveChangesAsync(ct);
        TempData["Message"] = "Saved.";
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
            TempData["Message"] = "This book is being converted right now. Try again in a minute.";
            return RedirectToAction(nameof(Index));
        }
        db.Books.Remove(book);
        log.Add(ActivityTypes.BookDeleted, AccessService.UserId(User), id, new { book.Title });
        await db.SaveChangesAsync(ct);
        storage.DeleteAll(id);
        TempData["Message"] = $"\"{book.Title}\" was deleted.";
        return RedirectToAction(nameof(Index));
    }

    private bool CanManage(Book book) =>
        AccessService.ReadsEverything(User) || book.UploadedById == AccessService.UserId(User);

    private async Task ValidateCategoriesAsync(UploadVm vm, CancellationToken ct)
    {
        if (vm.CategoryIds.Count == 0)
        {
            ModelState.AddModelError(nameof(vm.CategoryIds), "Pick at least one category, so readers can find the book.");
            return;
        }
        var ids = vm.CategoryIds.Distinct().ToList();
        if (await db.Categories.CountAsync(c => ids.Contains(c.Id), ct) != ids.Count)
            ModelState.AddModelError(nameof(vm.CategoryIds), "One of the categories no longer exists.");
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
