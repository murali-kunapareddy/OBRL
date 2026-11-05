using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Obrl.Web.Data;
using Obrl.Web.Domain;

namespace Obrl.Web.Security;

/// <summary>
/// Enforces OBRL's account rules on every signed-in request:
/// <list type="number">
/// <item>One active session per account: signing in elsewhere ends the older session.</item>
/// <item>Locked or deleted accounts are signed out at once.</item>
/// <item>A strong sign-in (an authenticator app or a passkey) must be set up before anything else can be used.</item>
/// <item>The latest disclaimer must be accepted before anything else can be used.</item>
/// </list>
/// </summary>
public static class SessionRules
{
    public const string SessionClaim = "obrl_sid";
    public const string ThemeCookie = "obrl_theme";
    public const string DisclaimerCacheKey = "obrl:disclaimer-version";

    private const string EnableAuthenticatorPath = "/Identity/Account/Manage/EnableAuthenticator";

    /// <summary>Paths a user may visit while 2FA setup or disclaimer acceptance is still pending.</summary>
    private static readonly string[] AlwaysAllowed =
    [
        "/Identity/Account/Logout",
        "/Identity/Account/Manage/EnableAuthenticator",
        "/Identity/Account/Manage/ShowRecoveryCodes",
        "/Identity/Account/Manage/ResetAuthenticator",
        "/Identity/Account/Manage/TwoFactorAuthentication",
        "/Identity/Account/Manage/Passkeys",
        "/Identity/Account/AccessDenied",
        "/Home/Error",
        "/Language",
        "/terms",
        "/versions",
    ];

    /// <summary>Called by the cookie handler whenever a session cookie is issued (sign-in or refresh).</summary>
    public static async Task OnSigningIn(CookieSigningInContext context)
    {
        if (context.Principal?.Identity is not ClaimsIdentity identity) return;
        var userId = identity.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId is null) return;

        var users = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(userId);
        if (user is null) return;

        var sessionId = Guid.NewGuid().ToString("N");
        user.CurrentSessionId = sessionId;
        user.LastLoginAt = DateTimeOffset.UtcNow;
        await users.UpdateAsync(user);

        foreach (var old in identity.FindAll(SessionClaim).ToList()) identity.RemoveClaim(old);
        identity.AddClaim(new Claim(SessionClaim, sessionId));

        context.Response.Cookies.Append(ThemeCookie, user.Theme, ThemeCookieOptions(context.Request.IsHttps));
        // The account's language follows the member to every device they sign in on.
        if (Localization.Languages.IsSupported(user.Language))
            Localization.Languages.WriteCookie(context.Response, user.Language!, context.Request.IsHttps);
    }

    /// <summary>
    /// Runs on every request that carries a session cookie: ends sessions idle for longer than the Admin's limit,
    /// then lets Identity re-check the security stamp as usual.
    /// </summary>
    public static async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var services = context.HttpContext.RequestServices;
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        if (services.GetRequiredService<IdleTimeout>().IsIdle(context, now))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            context.HttpContext.Response.Cookies.Append(IdleTimeout.SignedOutCookie, "1", new CookieOptions
            {
                HttpOnly = true, Secure = context.Request.IsHttps, SameSite = SameSiteMode.Lax, MaxAge = TimeSpan.FromMinutes(5),
            });
            return;
        }
        // Slide against the current limit, not the one the cookie was issued under: a cookie from before an
        // Admin shortened the limit would otherwise only renew at half its old, longer lifetime.
        services.GetRequiredService<IdleTimeout>().Slide(context, now);
        await SecurityStampValidator.ValidatePrincipalAsync(context);
    }

    /// <summary>Copies OBRL's own claims onto the principal Identity rebuilds when it re-validates the security stamp.</summary>
    public static Task KeepSessionClaims(SecurityStampRefreshingPrincipalContext context)
    {
        var sessionId = context.CurrentPrincipal?.FindFirstValue(SessionClaim);
        if (sessionId is not null && context.NewPrincipal?.Identity is ClaimsIdentity identity && !identity.HasClaim(c => c.Type == SessionClaim))
            identity.AddClaim(new Claim(SessionClaim, sessionId));
        return Task.CompletedTask;
    }

    public static CookieOptions ThemeCookieOptions(bool secure) => new()
    {
        HttpOnly = false,
        Secure = secure,
        SameSite = SameSiteMode.Lax,
        MaxAge = TimeSpan.FromDays(365),
        IsEssential = true,
    };

    /// <summary>Middleware: runs after authentication, before endpoints.</summary>
    public static async Task Enforce(HttpContext http, Func<Task> next)
    {
        if (http.User.Identity?.IsAuthenticated != true || IsStatic(http.Request.Path))
        {
            await next();
            return;
        }

        var users = http.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.GetUserAsync(http.User);
        var sessionId = http.User.FindFirstValue(SessionClaim);

        if (user is null || sessionId is null || sessionId != user.CurrentSessionId || await users.IsLockedOutAsync(user))
        {
            var reason = user?.CurrentSessionId is not null && sessionId != user.CurrentSessionId ? "elsewhere" : "ended";
            await http.SignOutAsync(IdentityConstants.ApplicationScheme);
            http.Response.Redirect($"/Identity/Account/Login?reason={reason}");
            return;
        }

        var path = http.Request.Path.Value ?? "/";

        // Security-relevant account changes (2FA turned on, password or role changed) update the security stamp.
        // Identity would only notice after its validation interval; OBRL asks for a fresh sign-in straight away.
        // Not applied before a strong sign-in exists, nor on the authenticator setup pages (creating the
        // authenticator key changes the stamp too, also for members who already have a passkey), so the
        // setup can finish and the recovery codes are shown once before signing in again.
        var db = http.RequestServices.GetRequiredService<ObrlDbContext>();
        var strongSignIn = await HasStrongSignInAsync(db, user);
        var stampClaim = http.RequestServices.GetRequiredService<IOptions<IdentityOptions>>().Value.ClaimsIdentity.SecurityStampClaimType;
        if (strongSignIn
            && http.User.FindFirstValue(stampClaim) != user.SecurityStamp
            && !path.StartsWith("/Identity/Account/Manage/ShowRecoveryCodes", StringComparison.OrdinalIgnoreCase)
            && !(path.StartsWith(EnableAuthenticatorPath, StringComparison.OrdinalIgnoreCase) && !user.TwoFactorEnabled))
        {
            await http.SignOutAsync(IdentityConstants.ApplicationScheme);
            http.Response.Redirect("/Identity/Account/Login?reason=changed");
            return;
        }
        var allowed = AlwaysAllowed.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));

        if (!strongSignIn && !allowed)
        {
            http.Response.Redirect(EnableAuthenticatorPath + "?required=1");
            return;
        }

        if (strongSignIn && !allowed && !path.StartsWith("/Disclaimer", StringComparison.OrdinalIgnoreCase))
        {
            var latest = await LatestDisclaimerVersionAsync(http.RequestServices);
            if (latest > 0)
            {
                var accepted = await db.TermsAcceptances.AnyAsync(t => t.UserId == user.Id && t.DisclaimerVersion == latest);
                if (!accepted)
                {
                    http.Response.Redirect("/Disclaimer?returnUrl=" + Uri.EscapeDataString(path + http.Request.QueryString));
                    return;
                }
            }
        }

        await next();
    }

    /// <summary>
    /// A strong sign-in is an authenticator app (password + code) or a passkey (device + fingerprint, face or PIN).
    /// Until one is set up, the account can only reach the setup pages.
    /// </summary>
    public static async Task<bool> HasStrongSignInAsync(ObrlDbContext db, ApplicationUser user) =>
        user.TwoFactorEnabled || await db.UserPasskeys.AnyAsync(p => p.UserId == user.Id);

    public static async Task<int> LatestDisclaimerVersionAsync(IServiceProvider services)
    {
        var cache = services.GetRequiredService<IMemoryCache>();
        if (cache.TryGetValue(DisclaimerCacheKey, out int version)) return version;
        var db = services.GetRequiredService<ObrlDbContext>();
        version = await db.Disclaimers.MaxAsync(d => (int?)d.Version) ?? 0;
        cache.Set(DisclaimerCacheKey, version, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) });
        return version;
    }

    private static bool IsStatic(PathString path) =>
        path.StartsWithSegments("/lib") || path.StartsWithSegments("/css") || path.StartsWithSegments("/js")
        || path.StartsWithSegments("/img") || path.Value == "/favicon.ico";
}
