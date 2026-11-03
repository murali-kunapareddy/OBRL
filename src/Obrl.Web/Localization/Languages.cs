using System.Globalization;
using Microsoft.AspNetCore.Localization;

namespace Obrl.Web.Localization;

/// <summary>A language members can pick for OBRL's interface. Book content is never translated.</summary>
/// <param name="Code">Culture code, e.g. "te". Must match the resource file name: Resources/SharedResource.{Code}.resx.</param>
/// <param name="NativeName">The language's name written in that language, as shown in the switcher.</param>
public sealed record Language(string Code, string NativeName);

/// <summary>
/// The interface languages. To add one, create Resources/SharedResource.{code}.resx with every key translated
/// (copy SharedResource.te.resx and replace the values), then add a line here. English needs no resource file:
/// the keys are the English text.
/// </summary>
public static class Languages
{
    public const string Default = "en";

    public static readonly IReadOnlyList<Language> All =
    [
        new("en", "English"),
        new("te", "తెలుగు"),
        new("kn", "ಕನ್ನಡ"),
    ];

    /// <summary>The language cookie. ASP.NET Core's culture provider reads it on every request.</summary>
    public static string CookieName => CookieRequestCultureProvider.DefaultCookieName;

    public static bool IsSupported(string? code) => code is not null && All.Any(l => l.Code == code);

    public static Language Current =>
        All.FirstOrDefault(l => l.Code == CultureInfo.CurrentUICulture.Name)
        ?? All.FirstOrDefault(l => l.Code == CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)
        ?? All[0];

    public static string CookieValue(string code) => CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(code));

    public static CookieOptions CookieOptions(bool secure) => new()
    {
        HttpOnly = true,
        Secure = secure,
        SameSite = SameSiteMode.Lax,
        IsEssential = true,
        MaxAge = TimeSpan.FromDays(365),
    };

    /// <summary>Remembers the language in this browser.</summary>
    public static void WriteCookie(HttpResponse response, string code, bool secure) =>
        response.Cookies.Append(CookieName, CookieValue(code), CookieOptions(secure));
}
