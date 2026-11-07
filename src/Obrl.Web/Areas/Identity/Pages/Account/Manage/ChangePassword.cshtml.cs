using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Obrl.Web.Domain;
using Obrl.Web.Services;

namespace Obrl.Web.Areas.Identity.Pages.Account.Manage;

/// <summary>
/// Change password. Also the first stop for a member with a temporary password from an Admin: until they choose
/// their own, every other page sends them here.
/// </summary>
public class ChangePasswordModel(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    ActivityLog log,
    IStringLocalizer<SharedResource> L) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();

    [TempData] public string? StatusMessage { get; set; }

    /// <summary>The member still has the temporary password an Admin gave them.</summary>
    public bool Required { get; private set; }

    public sealed class InputModel
    {
        [Required, DataType(DataType.Password), Display(Name = "Current password")]
        public string OldPassword { get; set; } = "";

        [Required, StringLength(128, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 12)]
        [DataType(DataType.Password), Display(Name = "New password")]
        public string NewPassword { get; set; } = "";

        [DataType(DataType.Password), Display(Name = "Confirm new password")]
        [Compare(nameof(NewPassword), ErrorMessage = "The new password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = "";
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return NotFound();
        Required = user.MustChangePassword;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return NotFound();
        Required = user.MustChangePassword;
        if (!ModelState.IsValid) return Page();

        if (Input.NewPassword == Input.OldPassword)
        {
            ModelState.AddModelError(string.Empty, L["Choose a password different from the current one."]);
            return Page();
        }
        var result = await users.ChangePasswordAsync(user, Input.OldPassword, Input.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, L[error.Description]);
            return Page();
        }
        var wasTemporary = user.MustChangePassword;
        if (wasTemporary)
        {
            user.MustChangePassword = false;
            await users.UpdateAsync(user);
        }
        await log.WriteAsync(ActivityTypes.PasswordChanged, user.Id, data: new { temporary = wasTemporary });
        await signIn.RefreshSignInAsync(user);
        // After a temporary password, the next step is setting up a strong sign-in (the session rules send them there).
        if (wasTemporary) return LocalRedirect("~/");
        StatusMessage = "Your password has been changed.";
        return RedirectToPage();
    }
}
