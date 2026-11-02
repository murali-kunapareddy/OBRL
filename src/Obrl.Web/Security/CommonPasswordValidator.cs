using Microsoft.AspNetCore.Identity;
using Obrl.Web.Domain;

namespace Obrl.Web.Security;

/// <summary>
/// Rejects passwords found in a list of commonly leaked passwords (Resources/common-passwords.txt:
/// the UK NCSC "100k most used passwords" list via SecLists, MIT licence), including simple variations
/// such as a common password with digits or symbols added ("password1234", "Summer2026!").
/// </summary>
public sealed class CommonPasswordValidator : IPasswordValidator<ApplicationUser>
{
    private static readonly Lazy<HashSet<string>> Common = new(() =>
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "common-passwords.txt");
        return File.Exists(path)
            ? File.ReadLines(path).Select(l => l.Trim()).Where(l => l.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];
    });

    public static bool IsCommon(string password)
    {
        var p = password.Trim();
        if (Common.Value.Contains(p)) return true;
        // Strip digits and symbols from both ends: "password1234" -> "password", "!!qwerty99" -> "qwerty".
        var core = p.Trim("0123456789!@#$%^&*()-_=+.,?~ ".ToCharArray());
        return core.Length >= 4 && Common.Value.Contains(core);
    }

    public Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
    {
        if (password is null) return Task.FromResult(IdentityResult.Success);
        if (IsCommon(password))
            return Task.FromResult(IdentityResult.Failed(new IdentityError
            {
                Code = "CommonPassword",
                Description = "This password appears in lists of leaked passwords. Choose another one.",
            }));

        var email = user.Email ?? user.UserName ?? "";
        var local = email.Split('@')[0];
        if (local.Length >= 4 && password.Contains(local, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(IdentityResult.Failed(new IdentityError
            {
                Code = "PasswordContainsEmail",
                Description = "The password must not contain your email name.",
            }));

        return Task.FromResult(IdentityResult.Success);
    }
}
