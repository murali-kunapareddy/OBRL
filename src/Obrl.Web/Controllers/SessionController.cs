using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Obrl.Web.Domain;
using Obrl.Web.Security;
using Obrl.Web.Services;

namespace Obrl.Web.Controllers;

/// <summary>Keeps an active member's session going, and ends it from the browser after inactivity.</summary>
[Route("session")]
public class SessionController(SignInManager<ApplicationUser> signIn, ActivityLog log) : Controller
{
    /// <summary>
    /// Sent by the page while the member scrolls, types, clicks or turns pages. Any signed-in request renews
    /// the session cookie once half the idle limit has passed, so this needs to do nothing else.
    /// </summary>
    [HttpPost("keepalive"), ValidateAntiForgeryToken]
    public IActionResult KeepAlive() => NoContent();

    /// <summary>Sent by the page when the member has been inactive for the whole idle limit: the book leaves the screen.</summary>
    [HttpPost("idle"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Idle()
    {
        var userId = AccessService.UserId(User);
        await signIn.SignOutAsync();
        await log.WriteAsync(ActivityTypes.IdleSignOut, userId);
        Response.Cookies.Append(IdleTimeout.SignedOutCookie, "1", new CookieOptions
        {
            HttpOnly = true, Secure = Request.IsHttps, SameSite = SameSiteMode.Lax, MaxAge = TimeSpan.FromMinutes(5),
        });
        return NoContent();
    }
}
