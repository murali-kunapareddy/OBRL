using Microsoft.EntityFrameworkCore;
using Obrl.Web.Data;
using Obrl.Web.Domain;

namespace Obrl.Web.Services;

/// <summary>One bar on a library chart. <see cref="Url"/> opens the matching books; <see cref="Note"/> is extra tooltip text.</summary>
public sealed record ChartItem(string Label, double Value, string? Url = null, string? Note = null);

/// <summary>The "Library at a glance" figures: how the books the member can see spread over languages, publishers and categories.</summary>
public sealed class LibraryOverview
{
    public int Books { get; init; }
    public int Languages { get; init; }
    public int Publishers { get; init; }
    public int Categories { get; init; }
    public int Readers { get; init; }
    public List<ChartItem> ByLanguage { get; init; } = [];
    public List<ChartItem> TopPublishers { get; init; } = [];
    public List<ChartItem> TopCategories { get; init; } = [];
    public List<ChartItem> MostRead { get; init; } = [];
    public List<ChartItem> TopRated { get; init; } = [];
    public bool IsEmpty => Books == 0;
}

public static class LibraryStats
{
    public const int Top = 10;

    /// <summary>
    /// Counts the books in each category, including the books of every sub category below it (a book in two
    /// sub categories of the same parent counts once for the parent).
    /// </summary>
    public static Dictionary<int, int> CountPerCategory(CategoryTree tree, IEnumerable<(Guid BookId, int CategoryId)> links)
    {
        var books = new Dictionary<int, HashSet<Guid>>();
        foreach (var (bookId, categoryId) in links)
        {
            foreach (var node in tree.Trail(categoryId))
            {
                if (!books.TryGetValue(node.Id, out var set)) books[node.Id] = set = [];
                set.Add(bookId);
            }
        }
        return books.ToDictionary(x => x.Key, x => x.Value.Count);
    }

    /// <summary>Builds the overview from the books the member can see. Names come in the member's language.</summary>
    public static async Task<LibraryOverview> BuildAsync(ObrlDbContext db, IQueryable<Book> visible, CategoryTree tree,
        List<(Guid BookId, int CategoryId)> links, Func<string, string> translate, BookText bookText, CancellationToken ct)
    {
        var total = await visible.CountAsync(ct);
        if (total == 0) return new LibraryOverview();
        var lang = BookText.CurrentLanguage;

        var languageCounts = await visible.GroupBy(b => b.LanguageId)
            .Select(g => new { Id = g.Key, Count = g.Count() }).ToListAsync(ct);
        var languageNames = await db.BookLanguages.AsNoTracking().ToDictionaryAsync(l => l.Id, l => l.Name, ct);
        var byLanguage = languageCounts
            .OrderByDescending(x => x.Count)
            .Select(x => x.Id is int id && languageNames.TryGetValue(id, out var name)
                ? new ChartItem(translate(name), x.Count)
                : new ChartItem(translate("Not set"), x.Count))
            .ToList();

        var publisherCounts = await visible.Where(b => b.PublisherId != null).GroupBy(b => b.PublisherId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).Take(Top).ToListAsync(ct);
        var publisherIds = publisherCounts.Select(x => x.Id).ToList();
        var publishers = await db.Publishers.AsNoTracking().Include(p => p.Translations)
            .Where(p => publisherIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var topPublishers = publisherCounts.Where(x => publishers.ContainsKey(x.Id))
            .Select(x => new ChartItem(PublisherText.Pick(publishers[x.Id], lang), x.Count)).ToList();

        // Categories rank by the books filed directly in them, so a parent does not win just by holding the others.
        var direct = links.GroupBy(l => l.CategoryId).Select(g => new { Id = g.Key, Count = g.Select(l => l.BookId).Distinct().Count() });
        var topCategories = direct.Where(x => tree.ById.ContainsKey(x.Id))
            .OrderByDescending(x => x.Count).ThenBy(x => tree.ById[x.Id].Name).Take(Top)
            .Select(x => new ChartItem(tree.ById[x.Id].Name, x.Count, $"/Library?category={x.Id}",
                string.Join(" › ", tree.Trail(x.Id).SkipLast(1).Select(c => c.Name)) is { Length: > 0 } parent ? parent : null))
            .ToList();

        // Readers: members who opened the book at least once (they have a saved reading place).
        var readCounts = await db.ReadingProgress.AsNoTracking()
            .Where(p => visible.Any(b => b.Id == p.BookId))
            .GroupBy(p => p.BookId).Select(g => new { Id = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).Take(Top).ToListAsync(ct);
        var rated = await visible.Where(b => b.RatingCount > 0)
            .OrderByDescending(b => b.RatingAverage).ThenByDescending(b => b.RatingCount).Take(Top)
            .Select(b => new { b.Id, b.RatingAverage, b.RatingCount }).ToListAsync(ct);
        var titleIds = readCounts.Select(x => x.Id).Concat(rated.Select(x => x.Id)).Distinct().ToList();
        var titles = await db.Books.AsNoTracking().Where(b => titleIds.Contains(b.Id)).Select(b => new { b.Id, b.Title }).ToDictionaryAsync(b => b.Id, b => b.Title, ct);
        var translated = await bookText.ForBooksAsync(titleIds, ct);
        string Title(Guid id) => translated.TryGetValue(id, out var t) && !string.IsNullOrWhiteSpace(t.Title) ? t.Title! : titles.GetValueOrDefault(id, "");

        return new LibraryOverview
        {
            Books = total,
            Languages = languageCounts.Count(x => x.Id is not null),
            Publishers = await visible.Where(b => b.PublisherId != null).Select(b => b.PublisherId).Distinct().CountAsync(ct),
            Categories = links.Select(l => l.CategoryId).Distinct().Count(),
            Readers = await db.ReadingProgress.Where(p => visible.Any(b => b.Id == p.BookId)).Select(p => p.UserId).Distinct().CountAsync(ct),
            ByLanguage = byLanguage,
            TopPublishers = topPublishers,
            TopCategories = topCategories,
            MostRead = readCounts.Select(x => new ChartItem(Title(x.Id), x.Count, $"/books/{x.Id}")).ToList(),
            TopRated = rated.Select(x => new ChartItem(Title(x.Id), Math.Round(x.RatingAverage, 2), $"/books/{x.Id}",
                translate(x.RatingCount == 1 ? "1 rating" : "{0} ratings").Replace("{0}", x.RatingCount.ToString()))).ToList(),
        };
    }
}
