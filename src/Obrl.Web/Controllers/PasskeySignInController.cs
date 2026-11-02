using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Obrl.Web.Domain;

namespace Obrl.Web.Controllers;

/// <summary>
/// Signing in with a passkey from the login page. The member does not type an email:
/// the browser offers the passkeys it has for this site and the server finds the account from the one chosen.
/// </summary>
[AllowAnonymous]
[Route("passkeys")]
public class PasskeySignInController(SignInManager<ApplicationUser> signIn) : Controller
{
    /// <summary>Step 1: a fresh challenge for navigator.credentials.get().</summary>
    [HttpPost("request-options"), ValidateAntiForgeryToken, EnableRateLimiting("posting")]
    public async Task<IActionResult> RequestOptions() =>
        Content(await signIn.MakePasskeyRequestOptionsAsync(null), "application/json");

    /// <summary>Step 2: check the browser's answer and start the session.</summary>
    [HttpPost("sign-in"), ValidateAntiForgeryToken, EnableRateLimiting("posting")]
    public async Task<IActionResult> SignIn(string? credential, string? returnUrl)
    {
        if (string.IsNullOrEmpty(credential)) return Redirect("/Identity/Account/Login?reason=passkey");
        var result = await signIn.PasskeySignInAsync(credential);
        if (result.IsLockedOut) return Redirect("/Identity/Account/Lockout");
        if (!result.Succeeded) return Redirect("/Identity/Account/Login?reason=passkey");
        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : Redirect("/");
    }
}
