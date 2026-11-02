using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Obrl.Web.Domain;
using Obrl.Web.Services;

namespace Obrl.Web.Security;

/// <summary>
/// Identity's sign-in manager with OBRL's rules: lockout is always on, and every sign-in
/// and failed attempt is written to the activity history.
/// </summary>
public sealed class ObrlSignInManager(
    UserManager<ApplicationUser> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<ApplicationUser>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<ApplicationUser> confirmation,
    IServiceProvider services)
    : SignInManager<ApplicationUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    public override async Task<SignInResult> PasswordSignInAsync(ApplicationUser user, string password, bool isPersistent, bool lockoutOnFailure)
    {
        // Never "remember me": sessions end after the idle timeout.
        var result = await base.PasswordSignInAsync(user, password, isPersistent: false, lockoutOnFailure: true);
        await LogAsync(user, result, "password");
        return result;
    }

    public override async Task<SignInResult> TwoFactorAuthenticatorSignInAsync(string code, bool isPersistent, bool rememberClient)
    {
        var user = await GetTwoFactorAuthenticationUserAsync();
        // Each device must enter a code at each sign-in: "remember this machine" is ignored.
        var result = await base.TwoFactorAuthenticatorSignInAsync(code, isPersistent: false, rememberClient: false);
        if (user is not null) await LogAsync(user, result, "authenticator");
        return result;
    }

    public override async Task<SignInResult> TwoFactorRecoveryCodeSignInAsync(string recoveryCode)
    {
        var user = await GetTwoFactorAuthenticationUserAsync();
        var result = await base.TwoFactorRecoveryCodeSignInAsync(recoveryCode);
        if (user is not null) await LogAsync(user, result, "recovery_code");
        return result;
    }

    public override async Task SignOutAsync()
    {
        var userId = Context.User.Identity?.IsAuthenticated == true ? UserManager.GetUserId(Context.User) : null;
        await base.SignOutAsync();
        if (userId is not null)
            await services.GetRequiredService<ActivityLog>().WriteAsync(ActivityTypes.Logout, userId);
    }

    private async Task LogAsync(ApplicationUser user, SignInResult result, string step)
    {
        var log = services.GetRequiredService<ActivityLog>();
        if (result.Succeeded)
            await log.WriteAsync(ActivityTypes.Login, user.Id, data: new { step });
        else if (!result.RequiresTwoFactor)
            await log.WriteAsync(ActivityTypes.LoginFailed, user.Id, data: new { step, lockedOut = result.IsLockedOut });
    }
}
