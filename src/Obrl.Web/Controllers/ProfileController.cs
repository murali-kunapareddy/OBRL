using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Obrl.Web.Domain;
using Obrl.Web.Security;

namespace Obrl.Web.Controllers;

public class ProfileController(UserManager<ApplicationUser> users) : Controller
{
    private static readonly HashSet<string> Themes = ["auto", "light", "dark", "sepia"];

    /// <summary>Saves the theme choice to the account so it follows the user to other devices.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Theme(string theme)
    {
        if (!Themes.Contains(theme)) return BadRequest();
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized();
        user.Theme = theme;
        await users.UpdateAsync(user);
        Response.Cookies.Append(SessionRules.ThemeCookie, theme, SessionRules.ThemeCookieOptions(Request.IsHttps));
        return NoContent();
    }
}
