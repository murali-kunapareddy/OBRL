using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Obrl.Web;
using Obrl.Web.Localization;

namespace Obrl.Tests;

public partial class LocalizationTests
{
    private static readonly string WebRoot = FindWebProject();

    public static TheoryData<string> TranslatedLanguages()
    {
        var data = new TheoryData<string>();
        foreach (var l in Languages.All.Where(l => l.Code != Languages.Default)) data.Add(l.Code);
        return data;
    }

    [Theory]
    [MemberData(nameof(TranslatedLanguages))]
    public void Every_interface_text_is_translated(string code)
    {
        var translated = ReadResx(code);
        var missing = UsedKeys().Where(k => !translated.ContainsKey(k)).ToList();
        Assert.True(missing.Count == 0,
            $"SharedResource.{code}.resx is missing {missing.Count} entries:\n" + string.Join("\n", missing));
    }

    [Theory]
    [MemberData(nameof(TranslatedLanguages))]
    public void Translations_keep_placeholders_and_markup(string code)
    {
        var broken = ReadResx(code)
            .Where(e => Signature(e.Key) != Signature(e.Value))
            .Select(e => $"{e.Key}  =>  {e.Value}")
            .ToList();
        Assert.True(broken.Count == 0, "Placeholders or HTML tags differ from the English text:\n" + string.Join("\n", broken));
    }

    [Theory]
    [MemberData(nameof(TranslatedLanguages))]
    public void Translations_are_built_into_the_app(string code)
    {
        var resources = new ResourceManager("Obrl.Web.Resources.SharedResource", typeof(SharedResource).Assembly);
        var expected = ReadResx(code)["Library"];
        Assert.Equal(expected, resources.GetString("Library", new CultureInfo(code)));
        Assert.NotEqual("Library", expected);
    }

    [Fact]
    public void Language_cookie_round_trips()
    {
        var value = Languages.CookieValue("te");
        var parsed = Microsoft.AspNetCore.Localization.CookieRequestCultureProvider.ParseCookieValue(value);
        Assert.Equal("te", parsed!.UICultures.Single().Value);
        Assert.True(Languages.IsSupported("kn"));
        Assert.False(Languages.IsSupported("xx"));
        Assert.False(Languages.IsSupported(null));
    }

    [Fact]
    public void Every_language_has_a_native_name_and_valid_culture()
    {
        foreach (var l in Languages.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(l.NativeName));
            Assert.Equal(l.Code, CultureInfo.GetCultureInfo(l.Code).Name);
        }
        Assert.Equal(Languages.All.Count, Languages.All.Select(l => l.Code).Distinct().Count());
    }

    /// <summary>Every key the app can ask for: literal L["..."] / H["..."] and Display names in the code, plus the run-time lists.</summary>
    private static HashSet<string> UsedKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var files = Directory.EnumerateFiles(WebRoot, "*.cs*", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains("Migrations"));
        foreach (var file in files)
        {
            // Lines that are only comments explain the L[...] convention; they are not text to translate.
            var text = string.Join('\n', File.ReadAllLines(file).Where(l => !l.TrimStart().StartsWith("///") && !l.TrimStart().StartsWith("@*")));
            foreach (Match m in LiteralKey().Matches(text)) keys.Add(Unescape(m.Groups[1].Value));
            foreach (Match m in DisplayName().Matches(text)) keys.Add(Unescape(m.Groups[1].Value));
        }
        keys.UnionWith(ExtraText.Script);
        keys.UnionWith(ExtraText.Names);
        keys.UnionWith(ExtraText.Identity);
        keys.UnionWith(ExtraText.Validation);
        return keys;
    }

    private static Dictionary<string, string> ReadResx(string code)
    {
        var path = Path.Combine(WebRoot, "Resources", $"SharedResource.{code}.resx");
        Assert.True(File.Exists(path), $"Missing {path}");
        return XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string)d.Element("value")!);
    }

    private static string Signature(string s) =>
        string.Join(" ", Placeholder().Matches(s).Select(m => m.Value).Order()) + " | " +
        string.Join(" ", Tag().Matches(s).Select(m => m.Value).Order());

    private static string Unescape(string s) => s.Replace("\\\"", "\"").Replace("\\\\", "\\");

    private static string FindWebProject()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Obrl.Web");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("src/Obrl.Web not found above " + AppContext.BaseDirectory);
    }

    [GeneratedRegex(@"\b[LH]\[""((?:[^""\\]|\\.)*)""")]
    private static partial Regex LiteralKey();

    [GeneratedRegex(@"Display\(Name = ""((?:[^""\\]|\\.)*)""")]
    private static partial Regex DisplayName();

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tag();
}
