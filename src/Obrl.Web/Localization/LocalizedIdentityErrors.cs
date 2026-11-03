using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Localization;

namespace Obrl.Web.Localization;

/// <summary>Identity's account error messages (passwords, emails and so on) in the member's language.</summary>
public sealed class LocalizedIdentityErrors(IStringLocalizer<SharedResource> L) : IdentityErrorDescriber
{
    private IdentityError Error(string code, LocalizedString text) => new() { Code = code, Description = text };

    public override IdentityError DefaultError() => Error(nameof(DefaultError), L["An unknown failure has occurred."]);
    public override IdentityError ConcurrencyFailure() => Error(nameof(ConcurrencyFailure), L["Someone else changed this account at the same time. Please try again."]);
    public override IdentityError PasswordMismatch() => Error(nameof(PasswordMismatch), L["Incorrect password."]);
    public override IdentityError InvalidUserName(string? userName) => Error(nameof(InvalidUserName), L["Username '{0}' is invalid.", userName ?? ""]);
    public override IdentityError InvalidEmail(string? email) => Error(nameof(InvalidEmail), L["Email '{0}' is invalid.", email ?? ""]);
    public override IdentityError DuplicateUserName(string userName) => Error(nameof(DuplicateUserName), L["Username '{0}' is already taken.", userName]);
    public override IdentityError DuplicateEmail(string email) => Error(nameof(DuplicateEmail), L["Email '{0}' is already taken.", email]);
    public override IdentityError UserAlreadyHasPassword() => Error(nameof(UserAlreadyHasPassword), L["This account already has a password."]);
    public override IdentityError PasswordTooShort(int length) => Error(nameof(PasswordTooShort), L["Passwords must be at least {0} characters.", length]);
    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) => Error(nameof(PasswordRequiresUniqueChars), L["Passwords must use at least {0} different characters.", uniqueChars]);
    public override IdentityError PasswordRequiresNonAlphanumeric() => Error(nameof(PasswordRequiresNonAlphanumeric), L["Passwords must have at least one character that is not a letter or digit."]);
    public override IdentityError PasswordRequiresDigit() => Error(nameof(PasswordRequiresDigit), L["Passwords must have at least one digit ('0'-'9')."]);
    public override IdentityError PasswordRequiresLower() => Error(nameof(PasswordRequiresLower), L["Passwords must have at least one lowercase letter ('a'-'z')."]);
    public override IdentityError PasswordRequiresUpper() => Error(nameof(PasswordRequiresUpper), L["Passwords must have at least one uppercase letter ('A'-'Z')."]);
}
