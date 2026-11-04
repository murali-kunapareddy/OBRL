using Microsoft.EntityFrameworkCore;
using Obrl.Web.Data;
using Obrl.Web.Models;

namespace Obrl.Web.Services;

/// <summary>Loads the whole category tree (small enough to hold in memory) and answers tree questions.</summary>
public sealed class CategoryTree
{
    public List<CategoryNode> Roots { get; } = [];
    public Dictionary<int, CategoryNode> ById { get; } = [];

    /// <summary>
    /// Loads every category with its name and description in the member's interface language
    /// (English where no translation was entered). Pass <paramref name="englishOnly"/> to skip translations.
    /// </summary>
    public static async Task<CategoryTree> LoadAsync(ObrlDbContext db, CancellationToken ct = default, bool englishOnly = false)
    {
        var rows = await db.Categories.AsNoTracking()
            .Select(c => new { c.Id, c.Name, c.ParentId, c.Path, c.Description, c.SortOrder })
            .ToListAsync(ct);

        var lang = englishOnly ? null : BookText.CurrentLanguage;
        var translations = lang is null ? []
            : await db.CategoryTranslations.AsNoTracking().Where(t => t.Language == lang).ToDictionaryAsync(t => t.CategoryId, ct);

        var tree = new CategoryTree();
        var nodes = rows.Select(r =>
            {
                var (name, description) = CategoryText.Pick(r.Name, r.Description, translations.GetValueOrDefault(r.Id));
                return (r.SortOrder, Node: new CategoryNode(r.Id, name, r.ParentId, r.Path, r.Path.Count(ch => ch == '/') - 2, description));
            })
            // Siblings sort by the moderator's order, then alphabetically in the language the member reads.
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Node.Name, StringComparer.Create(System.Globalization.CultureInfo.CurrentCulture, true))
            .Select(x => x.Node)
            .ToList();
        foreach (var n in nodes) tree.ById[n.Id] = n;
        foreach (var node in nodes)
        {
            if (node.ParentId is int p && tree.ById.TryGetValue(p, out var parent)) parent.Children.Add(node);
            else tree.Roots.Add(node);
        }
        return tree;
    }

    /// <summary>Root-first chain of categories ending at <paramref name="id"/>.</summary>
    public List<CategoryNode> Trail(int id)
    {
        var trail = new List<CategoryNode>();
        var current = ById.GetValueOrDefault(id);
        while (current is not null)
        {
            trail.Insert(0, current);
            current = current.ParentId is int p ? ById.GetValueOrDefault(p) : null;
        }
        return trail;
    }

    /// <summary>Every node in display order, depth-first, for drop-downs.</summary>
    public List<CategoryNode> Flatten()
    {
        var list = new List<CategoryNode>();
        void Walk(IEnumerable<CategoryNode> nodes)
        {
            foreach (var n in nodes) { list.Add(n); Walk(n.Children); }
        }
        Walk(Roots);
        return list;
    }

    public static string PathFor(string? parentPath, int id) => (parentPath ?? "/") + id + "/";
}
