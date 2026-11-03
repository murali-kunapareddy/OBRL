using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Obrl.Web.Domain;
using Obrl.Web.Localization;

namespace Obrl.Web.Controllers;

/// <summary>The language switcher. Open to everyone, so the sign-in page can be read in any language too.</summary>
[AllowAnonymous]
public class LanguageController(UserManager<ApplicationUser> users) : Controller
{
    /// <summary>Remembers the language in this browser and, for a signed-in member, on their account.</summary>
    [HttpPost("/Language"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Set(string culture, string? returnUrl)
    {
        if (!Languages.IsSupported(culture)) return BadRequest();
        Languages.WriteCookie(Response, culture, Request.IsHttps);

        if (User.Identity?.IsAuthenticated == true && await users.GetUserAsync(User) is { } user && user.Language != culture)
        {
            user.Language = culture;
            await users.UpdateAsync(user);
        }
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
    }
}
