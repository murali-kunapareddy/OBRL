using Microsoft.EntityFrameworkCore;
using Obrl.Web.Data;
using Obrl.Web.Domain;
using Obrl.Web.Localization;
using Obrl.Web.Models;

namespace Obrl.Web.Services;

/// <summary>A book's title, author and description as one reader sees them.</summary>
public sealed record LocalizedBook(string Title, string? Author, string? Description)
{
    /// <summary>Picks each field from the translation when it has one, otherwise the English text.</summary>
    public static LocalizedBook From(Book book, BookTranslation? t) => new(
        Pick(t?.Title) ?? book.Title,
        Pick(t?.Author) ?? book.Author,
        Pick(t?.Description) ?? book.Description);

    private static string? Pick(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}

/// <summary>
/// Shows book details in the member's interface language. Uploaders can enter the title, author and description in
/// every language OBRL offers; each field falls back to English (the text stored on the book) when it was left empty.
/// </summary>
public sealed class BookText(ObrlDbContext db)
{
    /// <summary>The language book details are shown in now, or null for English (nothing to look up).</summary>
    public static string? CurrentLanguage =>
        Languages.Current.Code is var code && code != Languages.Default ? code : null;

    /// <summary>Translations for the current language, keyed by book.</summary>
    public async Task<Dictionary<Guid, BookTranslation>> ForBooksAsync(IEnumerable<Guid> bookIds, CancellationToken ct)
    {
        var lang = CurrentLanguage;
        if (lang is null) return [];
        var ids = bookIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        return await db.BookTranslations.AsNoTracking()
            .Where(t => t.Language == lang && ids.Contains(t.BookId))
            .ToDictionaryAsync(t => t.BookId, ct);
    }

    public async Task<LocalizedBook> ForBookAsync(Book book, CancellationToken ct)
    {
        var lang = CurrentLanguage;
        var t = lang is null ? null
            : await db.BookTranslations.AsNoTracking().FirstOrDefaultAsync(x => x.BookId == book.Id && x.Language == lang, ct);
        return LocalizedBook.From(book, t);
    }

    /// <summary>Swaps the title and author on shelf cards for the member's language.</summary>
    public async Task<List<BookCard>> LocalizeAsync(List<BookCard> cards, CancellationToken ct) =>
        Localize(cards, await ForBooksAsync(cards.Select(c => c.Id), ct));

    /// <summary>Swaps the title and author on shelf cards using translations already loaded.</summary>
    public static List<BookCard> Localize(List<BookCard> cards, IReadOnlyDictionary<Guid, BookTranslation> map)
    {
        if (map.Count == 0) return cards;
        return cards.Select(c => map.TryGetValue(c.Id, out var t)
            ? c with
            {
                Title = string.IsNullOrWhiteSpace(t.Title) ? c.Title : t.Title,
                Author = string.IsNullOrWhiteSpace(t.Author) ? c.Author : t.Author,
            }
            : c).ToList();
    }

    /// <summary>
    /// Saves the translations from the upload form: blank entries are removed, languages OBRL doesn't offer are ignored.
    /// The book must be tracked with its <see cref="Book.Translations"/> loaded (or new).
    /// </summary>
    public static void Apply(Book book, IEnumerable<BookTranslationInput> input)
    {
        var now = DateTimeOffset.UtcNow;
        var wanted = input
            .Where(i => i.Language != Languages.Default && Languages.IsSupported(i.Language))
            .GroupBy(i => i.Language).Select(g => g.First())
            .ToDictionary(i => i.Language!);

        foreach (var existing in book.Translations.ToList())
        {
            if (!wanted.TryGetValue(existing.Language, out var i) || i.IsEmpty) book.Translations.Remove(existing);
        }
        foreach (var i in wanted.Values.Where(i => !i.IsEmpty))
        {
            var t = book.Translations.FirstOrDefault(x => x.Language == i.Language);
            if (t is null) book.Translations.Add(t = new BookTranslation { BookId = book.Id, Language = i.Language! });
            var (title, author, description) = (Clean(i.Title), Clean(i.Author), Clean(i.Description));
            if (t.Title != title || t.Author != author || t.Description != description) t.UpdatedAt = now;
            t.Title = title;
            t.Author = author;
            t.Description = description;
        }
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
