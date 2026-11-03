using Microsoft.Extensions.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Obrl.Web.Data;
using Obrl.Web.Domain;
using Obrl.Web.Models;
using Obrl.Web.Services;

namespace Obrl.Web.Controllers;

/// <summary>Moderators manage the category tree: add, rename, move and remove categories at any depth.</summary>
[Authorize(Policy = Policies.CanModerate)]
public class CategoriesController(ObrlDbContext db, IStringLocalizer<SharedResource> L) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewBag.BookCounts = await db.BookCategories.GroupBy(bc => bc.CategoryId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return View((await CategoryTree.LoadAsync(db, ct)).Roots);
    }

    [HttpGet]
    public async Task<IActionResult> Create(int? parentId, CancellationToken ct) =>
        View("Edit", new CategoryEditVm { ParentId = parentId, AllCategories = (await CategoryTree.LoadAsync(db, ct)).Flatten() });

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CategoryEditVm vm, CancellationToken ct)
    {
        var parent = vm.ParentId is int pid ? await db.Categories.FindAsync([pid], ct) : null;
        if (vm.ParentId is not null && parent is null) ModelState.AddModelError(nameof(vm.ParentId), L["Parent not found."]);
        await CheckUniqueNameAsync(vm, ct);
        if (!ModelState.IsValid)
        {
            vm.AllCategories = (await CategoryTree.LoadAsync(db, ct)).Flatten();
            return View("Edit", vm);
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var category = new Category
        {
            Name = vm.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(vm.Description) ? null : vm.Description.Trim(),
            ParentId = parent?.Id,
            SortOrder = vm.SortOrder,
            Path = "/pending/",
        };
        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);
        category.Path = CategoryTree.PathFor(parent?.Path, category.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        TempData["Message"] = L["Added “{0}”.", category.Name].Value;
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var c = await db.Categories.FindAsync([id], ct);
        if (c is null) return NotFound();
        var tree = await CategoryTree.LoadAsync(db, ct);
        return View(new CategoryEditVm
        {
            Id = c.Id,
            Name = c.Name,
            Description = c.Description,
            ParentId = c.ParentId,
            SortOrder = c.SortOrder,
            AllCategories = tree.Flatten().Where(n => !n.Path.StartsWith(c.Path, StringComparison.Ordinal)).ToList(),
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, CategoryEditVm vm, CancellationToken ct)
    {
        var c = await db.Categories.FindAsync([id], ct);
        if (c is null) return NotFound();
        vm.Id = id;
        var parent = vm.ParentId is int pid ? await db.Categories.FindAsync([pid], ct) : null;
        if (vm.ParentId is not null && parent is null) ModelState.AddModelError(nameof(vm.ParentId), L["Parent not found."]);
        if (parent is not null && parent.Path.StartsWith(c.Path, StringComparison.Ordinal))
            ModelState.AddModelError(nameof(vm.ParentId), L["A category cannot be moved inside itself."]);
        await CheckUniqueNameAsync(vm, ct);
        if (!ModelState.IsValid)
        {
            vm.AllCategories = (await CategoryTree.LoadAsync(db, ct)).Flatten().Where(n => !n.Path.StartsWith(c.Path, StringComparison.Ordinal)).ToList();
            return View(vm);
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        c.Name = vm.Name.Trim();
        c.Description = string.IsNullOrWhiteSpace(vm.Description) ? null : vm.Description.Trim();
        c.SortOrder = vm.SortOrder;
        if (c.ParentId != parent?.Id)
        {
            // Moving: rewrite this category's path and every descendant's.
            var oldPath = c.Path;
            var newPath = CategoryTree.PathFor(parent?.Path, c.Id);
            var subtree = await db.Categories.Where(x => x.Path.StartsWith(oldPath)).ToListAsync(ct);
            foreach (var node in subtree) node.Path = newPath + node.Path[oldPath.Length..];
            c.ParentId = parent?.Id;
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        TempData["Message"] = L["Saved “{0}”.", c.Name].Value;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var c = await db.Categories.FindAsync([id], ct);
        if (c is null) return NotFound();
        if (await db.Categories.AnyAsync(x => x.ParentId == id, ct))
            TempData["Message"] = L["“{0}” has sub categories. Move or delete them first.", c.Name].Value;
        else if (await db.BookCategories.AnyAsync(x => x.CategoryId == id, ct))
            TempData["Message"] = L["“{0}” still has books. Move them to another category first.", c.Name].Value;
        else
        {
            db.Categories.Remove(c);
            await db.SaveChangesAsync(ct);
            TempData["Message"] = L["Deleted “{0}”.", c.Name].Value;
        }
        return RedirectToAction(nameof(Index));
    }

    private async Task CheckUniqueNameAsync(CategoryEditVm vm, CancellationToken ct)
    {
        var name = vm.Name?.Trim() ?? "";
        if (await db.Categories.AnyAsync(x => x.ParentId == vm.ParentId && x.Name == name && x.Id != vm.Id, ct))
            ModelState.AddModelError(nameof(vm.Name), L["A category with this name already exists here."]);
    }
}
