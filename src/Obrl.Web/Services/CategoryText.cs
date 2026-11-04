using Obrl.Web.Domain;
using Obrl.Web.Localization;
using Obrl.Web.Models;

namespace Obrl.Web.Services;

/// <summary>
/// Category names and descriptions in other interface languages. English is stored on the category and is required;
/// every other language is optional, and each empty field shows the English text.
/// </summary>
public static class CategoryText
{
    /// <summary>The name and description one reader sees: the translation's field when it has one, otherwise English.</summary>
    public static (string Name, string? Description) Pick(string name, string? description, CategoryTranslation? t) =>
        (Clean(t?.Name) ?? name, Clean(t?.Description) ?? description);

    /// <summary>
    /// Saves the translations from the category form: blank entries are removed, languages OBRL doesn't offer are ignored.
    /// The category must be tracked with its <see cref="Category.Translations"/> loaded (or new).
    /// </summary>
    public static void Apply(Category category, IEnumerable<CategoryTranslationInput> input)
    {
        var now = DateTimeOffset.UtcNow;
        var wanted = input
            .Where(i => i.Language != Languages.Default && Languages.IsSupported(i.Language))
            .GroupBy(i => i.Language).Select(g => g.First())
            .ToDictionary(i => i.Language!);

        foreach (var existing in category.Translations.ToList())
        {
            if (!wanted.TryGetValue(existing.Language, out var i) || i.IsEmpty) category.Translations.Remove(existing);
        }
        foreach (var i in wanted.Values.Where(i => !i.IsEmpty))
        {
            var t = category.Translations.FirstOrDefault(x => x.Language == i.Language);
            if (t is null) category.Translations.Add(t = new CategoryTranslation { CategoryId = category.Id, Language = i.Language! });
            var (name, description) = (Clean(i.Name), Clean(i.Description));
            if (t.Name != name || t.Description != description) t.UpdatedAt = now;
            t.Name = name;
            t.Description = description;
        }
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
