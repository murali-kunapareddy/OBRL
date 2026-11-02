using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Obrl.Web.Domain;
using Obrl.Web.Services;

namespace Obrl.Web.Security;

/// <summary>
/// Identity's sign-in manager with OBRL's rules: lockout is always on, a password alone never
/// signs in an account that has a strong sign-in, and every sign-in and failed attempt is
/// written to the activity history.
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
    /// <summary>Set on the request when the password was right but the account signs in with a passkey only.</summary>
    public const string PasskeyOnlyItem = "obrl:passkey-only";

    public override async Task<SignInResult> PasswordSignInAsync(ApplicationUser user, string password, bool isPersistent, bool lockoutOnFailure)
    {
        // A member who set up a passkey but no authenticator app has no second step after the password,
        // so the password alone must not open a session: they sign in with the passkey instead.
        if (!user.TwoFactorEnabled && (await UserManager.GetPasskeysAsync(user)).Count > 0)
        {
            var check = await CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
            var outcome = check.Succeeded ? SignInResult.NotAllowed : check;
            if (check.Succeeded) Context.Items[PasskeyOnlyItem] = true;
            await LogAsync(user, outcome, "password");
            return outcome;
        }

        // Never "remember me": sessions end after the idle timeout.
        var result = await base.PasswordSignInAsync(user, password, isPersistent: false, lockoutOnFailure: true);
        await LogAsync(user, result, "password");
        return result;
    }

    public override async Task<SignInResult> PasskeySignInAsync(string credentialJson)
    {
        SignInResult result;
        try
        {
            result = await base.PasskeySignInAsync(credentialJson);
        }
        catch (InvalidOperationException)
        {
            // No challenge was issued to this browser (a replayed or hand-made request).
            result = SignInResult.Failed;
        }
        var user = await FindPasskeyOwnerAsync(credentialJson);
        if (user is not null) await LogAsync(user, result, "passkey");
        else if (!result.Succeeded)
            await services.GetRequiredService<ActivityLog>().WriteAsync(ActivityTypes.LoginFailed, null, data: new { step = "passkey", unknownCredential = true });
        return result;
    }

    private async Task<ApplicationUser?> FindPasskeyOwnerAsync(string credentialJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(credentialJson);
            var id = doc.RootElement.GetProperty("id").GetString();
            return id is null ? null : await UserManager.FindByPasskeyIdAsync(Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(id));
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
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
