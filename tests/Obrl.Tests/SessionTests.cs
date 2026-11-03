using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Obrl.Web.Data;
using Obrl.Web.Security;

namespace Obrl.Tests;

public class SessionTests
{
    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, IdentityConstants.ApplicationScheme));

    [Fact]
    public async Task Stamp_refresh_keeps_the_session_id()
    {
        // Regression: Identity's 5-minute refresh rebuilt the sign-in without OBRL's session id,
        // so the single-session check signed active members out.
        var current = Principal(new Claim(ClaimTypes.NameIdentifier, "u1"), new Claim(SessionRules.SessionClaim, "abc"));
        var fresh = Principal(new Claim(ClaimTypes.NameIdentifier, "u1"));
        await SessionRules.KeepSessionClaims(new SecurityStampRefreshingPrincipalContext { CurrentPrincipal = current, NewPrincipal = fresh });
        Assert.Equal("abc", fresh.FindFirstValue(SessionRules.SessionClaim));
    }

    [Fact]
    public async Task Stamp_refresh_does_not_duplicate_the_session_id()
    {
        var current = Principal(new Claim(SessionRules.SessionClaim, "abc"));
        var fresh = Principal(new Claim(SessionRules.SessionClaim, "abc"));
        await SessionRules.KeepSessionClaims(new SecurityStampRefreshingPrincipalContext { CurrentPrincipal = current, NewPrincipal = fresh });
        Assert.Single(fresh.FindAll(SessionRules.SessionClaim));
    }

    private static async Task<IdleTimeout> IdleAsync(string? stored, int configured = 30)
    {
        using var db = new ObrlDbContext(new DbContextOptionsBuilder<ObrlDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        if (stored is not null)
        {
            db.SiteSettings.Add(new Obrl.Web.Domain.SiteSetting { Key = IdleTimeout.SettingKey, Value = stored });
            await db.SaveChangesAsync();
        }
        var idle = new IdleTimeout(new OptionsCache<CookieAuthenticationOptions>());
        await idle.LoadAsync(db, configured);
        return idle;
    }

    [Fact]
    public async Task Admin_choice_wins_over_configuration() =>
        Assert.Equal(10, (await IdleAsync("10")).Minutes);

    [Fact]
    public async Task Configuration_applies_until_an_admin_chooses() =>
        Assert.Equal(30, (await IdleAsync(null)).Minutes);

    [Theory]
    [InlineData(5, true)]
    [InlineData(15, true)]
    [InlineData(7, false)]
    [InlineData(0, false)]
    public void Only_listed_choices_are_allowed(int minutes, bool allowed) =>
        Assert.Equal(allowed, IdleTimeout.IsAllowed(minutes));

    private static CookieValidatePrincipalContext Context(DateTimeOffset issued, DateTimeOffset expires) =>
        new(new DefaultHttpContext(),
            new AuthenticationScheme(IdentityConstants.ApplicationScheme, null, typeof(CookieAuthenticationHandler)),
            new CookieAuthenticationOptions(),
            new AuthenticationTicket(Principal(), new AuthenticationProperties { IssuedUtc = issued, ExpiresUtc = expires }, IdentityConstants.ApplicationScheme));

    [Theory]
    // limit, minutes since renewal, minutes the cookie was issued for, renew?
    [InlineData("5", 1, 5, false)]
    [InlineData("5", 3, 5, true)]   // past half the limit
    [InlineData("5", 3, 30, true)]  // issued before an Admin shortened the limit
    [InlineData("30", 1, 5, true)]  // issued before an Admin lengthened the limit: would expire too soon
    [InlineData("30", 10, 30, false)]
    public async Task Cookie_slides_against_the_current_limit(string limit, int minutesAgo, int issuedFor, bool renew)
    {
        var idle = await IdleAsync(limit);
        var now = DateTimeOffset.Parse("2026-11-03T08:00:00Z");
        var context = Context(now.AddMinutes(-minutesAgo), now.AddMinutes(issuedFor - minutesAgo));
        idle.Slide(context, now);
        Assert.Equal(renew, context.ShouldRenew);
        if (renew) Assert.Equal(now + idle.Span, context.Properties.ExpiresUtc);
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(6, true)]
    public async Task Session_is_idle_once_the_limit_passes_since_last_renewal(int minutesAgo, bool idle)
    {
        var timeout = await IdleAsync("5");
        var now = DateTimeOffset.Parse("2026-11-03T08:00:00Z");
        var props = new AuthenticationProperties { IssuedUtc = now.AddMinutes(-minutesAgo), ExpiresUtc = now.AddMinutes(30 - minutesAgo) };
        var context = new CookieValidatePrincipalContext(new DefaultHttpContext(),
            new AuthenticationScheme(IdentityConstants.ApplicationScheme, null, typeof(CookieAuthenticationHandler)),
            new CookieAuthenticationOptions(),
            new AuthenticationTicket(Principal(), props, IdentityConstants.ApplicationScheme));
        Assert.Equal(idle, timeout.IsIdle(context, now));
    }
}
