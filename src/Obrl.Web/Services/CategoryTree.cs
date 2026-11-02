using Microsoft.EntityFrameworkCore;
using Obrl.Web.Data;
using Obrl.Web.Models;

namespace Obrl.Web.Services;

/// <summary>Loads the whole category tree (small enough to hold in memory) and answers tree questions.</summary>
public sealed class CategoryTree
{
    public List<CategoryNode> Roots { get; } = [];
    public Dictionary<int, CategoryNode> ById { get; } = [];

    public static async Task<CategoryTree> LoadAsync(ObrlDbContext db, CancellationToken ct = default)
    {
        var rows = await db.Categories.AsNoTracking()
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new { c.Id, c.Name, c.ParentId, c.Path, c.Description })
            .ToListAsync(ct);

        var tree = new CategoryTree();
        foreach (var r in rows)
        {
            var depth = r.Path.Count(ch => ch == '/') - 2;
            tree.ById[r.Id] = new CategoryNode(r.Id, r.Name, r.ParentId, r.Path, depth, r.Description);
        }
        foreach (var node in rows.Select(r => tree.ById[r.Id]))
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
