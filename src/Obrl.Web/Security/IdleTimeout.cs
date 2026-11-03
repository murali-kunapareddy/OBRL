using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Obrl.Web.Data;
using Obrl.Web.Domain;

namespace Obrl.Web.Security;

/// <summary>
/// How long a member can be inactive before OBRL signs them out. Admins pick it under Admin &gt; Settings;
/// until they do, <c>Obrl:IdleTimeoutMinutes</c> from configuration applies.
/// Activity means any request, plus the keep-alive the pages send while someone scrolls, types,
/// clicks or turns pages, so reading a book counts as activity.
/// </summary>
public sealed class IdleTimeout(IOptionsMonitorCache<CookieAuthenticationOptions> cookieOptions)
{
    public const string SettingKey = "IdleTimeoutMinutes";
    public static readonly int[] Choices = [5, 10, 15, 30, 60];

    /// <summary>Set on the response when a session ended for inactivity, so the login page can say why.</summary>
    public const string SignedOutCookie = "obrl_idle";

    private volatile int _minutes = 30;

    public int Minutes => _minutes;
    public TimeSpan Span => TimeSpan.FromMinutes(_minutes);

    public static bool IsAllowed(int minutes) => Choices.Contains(minutes);

    /// <summary>Reads the Admin's choice at start-up, falling back to configuration.</summary>
    public async Task LoadAsync(ObrlDbContext db, int configured)
    {
        var stored = await db.SiteSettings.AsNoTracking().Where(s => s.Key == SettingKey).Select(s => s.Value).FirstOrDefaultAsync();
        Apply(int.TryParse(stored, out var m) && m > 0 ? m : configured);
    }

    public async Task SaveAsync(ObrlDbContext db, int minutes, string adminId)
    {
        if (!IsAllowed(minutes)) throw new ArgumentOutOfRangeException(nameof(minutes));
        var row = await db.SiteSettings.FirstOrDefaultAsync(s => s.Key == SettingKey);
        if (row is null) db.SiteSettings.Add(row = new SiteSetting { Key = SettingKey });
        row.Value = minutes.ToString(System.Globalization.CultureInfo.InvariantCulture);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.UpdatedById = adminId;
        await db.SaveChangesAsync();
        Apply(minutes);
    }

    private void Apply(int minutes)
    {
        _minutes = Math.Clamp(minutes, 1, 24 * 60);
        // The cookie handler reads its options per request; dropping the cached copy makes the new expiry apply at once.
        cookieOptions.TryRemove(IdentityConstants.ApplicationScheme);
    }

    /// <summary>
    /// Cookie check on every request: a session whose cookie was last renewed longer ago than the idle limit ends,
    /// even if the cookie itself was issued under a longer limit before an Admin shortened it.
    /// Sliding expiration renews the cookie once half the limit has passed, so active members never hit this.
    /// </summary>
    public bool IsIdle(CookieValidatePrincipalContext context, DateTimeOffset now) =>
        context.Properties.IssuedUtc is { } issued && now - issued > Span;

    /// <summary>
    /// Renews the cookie once half the current limit has passed since it was last renewed, or when it would
    /// expire within half the limit (it was issued under a shorter limit).
    /// </summary>
    public void Slide(CookieValidatePrincipalContext context, DateTimeOffset now)
    {
        if (context.Properties.IssuedUtc is { } issued && now - issued <= Span / 2
            && context.Properties.ExpiresUtc is { } expires && expires - now >= Span / 2)
            return;
        context.Properties.IssuedUtc = now;
        context.Properties.ExpiresUtc = now + Span;
        context.ShouldRenew = true;
    }
}
