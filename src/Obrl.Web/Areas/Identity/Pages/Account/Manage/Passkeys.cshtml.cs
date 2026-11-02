using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Obrl.Web.Domain;
using Obrl.Web.Services;

namespace Obrl.Web.Areas.Identity.Pages.Account.Manage;

/// <summary>Add, rename and remove the passkeys a member signs in with.</summary>
public class PasskeysModel(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, ActivityLog log) : PageModel
{
    public const int MaxPasskeys = 10;

    public sealed record Row(string Id, string Name, DateTimeOffset CreatedAt, bool Synced);

    public List<Row> Passkeys { get; private set; } = [];
    public bool HasAuthenticator { get; private set; }
    public bool Required { get; private set; }

    [TempData] public string? StatusMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(bool required = false)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        Required = required;
        await LoadAsync(user);
        return Page();
    }

    /// <summary>Step 1 of adding a passkey: the options the browser passes to navigator.credentials.create().</summary>
    public async Task<IActionResult> OnPostCreationOptionsAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        if ((await users.GetPasskeysAsync(user)).Count >= MaxPasskeys) return BadRequest();
        var options = await signIn.MakePasskeyCreationOptionsAsync(new PasskeyUserEntity
        {
            Id = user.Id,
            Name = user.Email ?? user.UserName ?? user.Id,
            DisplayName = string.IsNullOrWhiteSpace(user.DisplayName) ? (user.Email ?? "OBRL member") : user.DisplayName,
        });
        return Content(options, "application/json");
    }

    /// <summary>Step 2: the browser's answer is checked and the passkey saved.</summary>
    public async Task<IActionResult> OnPostAddAsync(string? credential, string? name)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (string.IsNullOrEmpty(credential))
        {
            ErrorMessage = "Your browser did not create a passkey. Please try again.";
            return RedirectToPage();
        }
        if ((await users.GetPasskeysAsync(user)).Count >= MaxPasskeys)
        {
            ErrorMessage = $"You can have up to {MaxPasskeys} passkeys. Remove one first.";
            return RedirectToPage();
        }

        PasskeyAttestationResult attestation;
        try
        {
            attestation = await signIn.PerformPasskeyAttestationAsync(credential);
        }
        catch (InvalidOperationException)
        {
            // No creation was started in this browser session (an old tab or a hand-made request).
            ErrorMessage = "This passkey request expired. Please try again.";
            return RedirectToPage();
        }
        if (!attestation.Succeeded)
        {
            ErrorMessage = "This passkey could not be added: " + attestation.Failure.Message;
            return RedirectToPage();
        }

        var passkey = attestation.Passkey;
        passkey.Name = CleanName(name) ?? DefaultName(passkey);
        var saved = await users.AddOrUpdatePasskeyAsync(user, passkey);
        if (!saved.Succeeded)
        {
            ErrorMessage = "This passkey could not be saved. " + string.Join(" ", saved.Errors.Select(e => e.Description));
            return RedirectToPage();
        }

        await log.WriteAsync(ActivityTypes.PasskeyAdded, user.Id, data: new { name = passkey.Name, synced = passkey.IsBackedUp });
        // Saving changes the security stamp; refresh the session so it stays valid.
        await signIn.RefreshSignInAsync(user);
        StatusMessage = $"Passkey \"{passkey.Name}\" added. Next time, choose \"Sign in with a passkey\".";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRenameAsync(string id, string? name)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        var passkey = await FindAsync(user, id);
        var clean = CleanName(name);
        if (passkey is null || clean is null) return RedirectToPage();
        passkey.Name = clean;
        await users.AddOrUpdatePasskeyAsync(user, passkey);
        await signIn.RefreshSignInAsync(user);
        StatusMessage = "Passkey renamed.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveAsync(string id)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        var passkey = await FindAsync(user, id);
        if (passkey is null) return RedirectToPage();

        await users.RemovePasskeyAsync(user, passkey.CredentialId);
        await log.WriteAsync(ActivityTypes.PasskeyRemoved, user.Id, data: new { name = passkey.Name });
        await signIn.RefreshSignInAsync(user);
        StatusMessage = $"Passkey \"{passkey.Name}\" removed. Also delete it from your device or password manager.";
        return RedirectToPage();
    }

    private async Task<UserPasskeyInfo?> FindAsync(ApplicationUser user, string id)
    {
        try
        {
            return await users.GetPasskeyAsync(user, WebEncoders.Base64UrlDecode(id));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private async Task LoadAsync(ApplicationUser user)
    {
        HasAuthenticator = user.TwoFactorEnabled;
        Passkeys = (await users.GetPasskeysAsync(user))
            .OrderBy(p => p.CreatedAt)
            .Select(p => new Row(WebEncoders.Base64UrlEncode(p.CredentialId), p.Name ?? "Passkey", p.CreatedAt, p.IsBackedUp))
            .ToList();
    }

    private static string? CleanName(string? name) => Community.CleanText(name?.Replace('\n', ' '), 60);

    private static string DefaultName(UserPasskeyInfo p) =>
        p.IsBackedUp ? "Synced passkey" : "Passkey on this device";
}
