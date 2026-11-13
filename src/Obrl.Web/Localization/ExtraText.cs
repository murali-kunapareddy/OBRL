namespace Obrl.Web.Localization;

/// <summary>
/// Text that reaches a translation at run time rather than as a literal L["..."] in OBRL's code:
/// messages and labels from ASP.NET Core Identity's own pages, enum and role names, and the scripts' text.
/// Listed here so translators (and the resource test) see every key.
/// </summary>
public static class ExtraText
{
    /// <summary>Text OBRL's scripts use through obrl.t(). Sent to the page by Views/Shared/_ScriptText.cshtml.</summary>
    public static readonly string[] Script =
    [
        // session.js
        "You will be signed out in a minute because nothing has happened for a while.", "Stay signed in",
        // passkeys.js
        "The server did not answer ({0}).", "The passkey prompt was cancelled or timed out.",
        "This device already has a passkey for your account.", "Something went wrong: {0}",
        // reader.js
        "Printing is not available", "Page {0}", "Section {0}", "No bookmarks yet.", "Remove bookmark",
        "Optional note for this bookmark:", "Bookmark added", "You are turning pages very fast. Please wait a moment.",
        "This page could not be loaded. Try again.", "You are moving very fast. Please wait a moment.",
        "This part could not be loaded. Try again.", "No contents list.",
        "Tap the middle to show or hide the bars. Tap the left or right edge to turn the page.",
    ];

    /// <summary>Names shown through L[value]: book statuses and roles.</summary>
    public static readonly string[] Names =
    [
        "Queued", "Processing", "Ready", "Failed",
        "User", "Contributor", "Moderator", "Admin",
        // Book languages seeded by the BookMetadataAndEmail migration (others Admins add show as typed)
        "English", "Telugu", "Hindi", "Kannada",
        // Library at a glance (Services/LibraryStats.cs)
        "Not set", "1 rating", "{0} ratings",
    ];

    /// <summary>Labels, validation and status messages written inside Identity's page models.</summary>
    public static readonly string[] Identity =
    [
        "Email", "Password", "Remember me?", "Authenticator code", "Remember this machine", "Recovery Code",
        "Current password", "New password", "Confirm new password", "Phone number", "Verification Code", "Username",
        "The {0} must be at least {2} and at max {1} characters long.",
        "The new password and confirmation password do not match.",
        "Invalid login attempt.", "Invalid authenticator code.", "Invalid recovery code entered.", "Verification code is invalid.",
        "Your profile has been updated", "Unexpected error when trying to set phone number.", "Your password has been changed.",
        "Your authenticator app has been verified.",
        "Your authenticator app key has been reset, you will need to configure your authenticator app using the new key.",
        "You have generated new recovery codes.",
        "The current browser has been forgotten. When you login again from this browser you will be prompted for your 2fa code.",
        "This password appears in lists of leaked passwords. Choose another one.",
        "The password must not contain your email name.",
    ];

    /// <summary>Messages from <see cref="DefaultValidationMessages"/>, used by every model without its own message.</summary>
    public static readonly string[] Validation =
    [
        DefaultValidationMessages.Required, DefaultValidationMessages.MaxLength, DefaultValidationMessages.LengthRange,
        DefaultValidationMessages.Email, DefaultValidationMessages.Phone, DefaultValidationMessages.Range, DefaultValidationMessages.Compare,
    ];
}
