using Obrl.Web.Domain;
using Obrl.Web.Localization;

namespace Obrl.Web.Services;

/// <summary>
/// Publisher names in other interface languages. English is stored on the publisher and is required; every other
/// language is optional, and a missing one shows the English name.
/// </summary>
public static class PublisherText
{
    /// <summary>The name one reader sees: the name in <paramref name="language"/> when there is one, otherwise English.</summary>
    public static string Pick(Publisher publisher, string? language) =>
        language is null ? publisher.Name
            : Clean(publisher.Translations.FirstOrDefault(t => t.Language == language)?.Name) ?? publisher.Name;

    /// <summary>The name in the member's current interface language. Needs <see cref="Publisher.Translations"/> loaded.</summary>
    public static string ForCurrentLanguage(Publisher publisher) => Pick(publisher, BookText.CurrentLanguage);

    /// <summary>
    /// Sets the publisher's names from a form. <paramref name="names"/> maps a language code to the typed name.
    /// A filled-in name is saved. An empty one removes the saved name only when <paramref name="emptyRemoves"/> is true
    /// (the form showed the saved names, so emptying a box means "remove it").
    /// Languages OBRL doesn't offer, and English, are ignored. The publisher must be tracked with its translations loaded (or new).
    /// </summary>
    public static void Apply(Publisher publisher, IEnumerable<KeyValuePair<string?, string?>> names, bool emptyRemoves)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (language, typed) in names
                     .Where(n => n.Key != Languages.Default && Languages.IsSupported(n.Key))
                     .GroupBy(n => n.Key).Select(g => g.First()))
        {
            var name = Clean(typed);
            var existing = publisher.Translations.FirstOrDefault(t => t.Language == language);
            if (name is null)
            {
                if (existing is not null && emptyRemoves) publisher.Translations.Remove(existing);
                continue;
            }
            if (name.Length > 200) name = name[..200];
            if (existing is null)
                publisher.Translations.Add(new PublisherTranslation { PublisherId = publisher.Id, Language = language!, Name = name, UpdatedAt = now });
            else if (existing.Name != name)
            {
                existing.Name = name;
                existing.UpdatedAt = now;
            }
        }
    }

    /// <summary>Trims and collapses spaces; null for an empty name.</summary>
    public static string? Clean(string? s)
    {
        var clean = string.Join(' ', (s ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length == 0 ? null : clean;
    }
}
