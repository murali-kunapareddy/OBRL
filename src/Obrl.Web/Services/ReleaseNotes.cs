namespace Obrl.Web.Services;

/// <summary>One release on the version history page (/versions), newest first.</summary>
public sealed record Release(string Version, DateOnly Date, string Title, string[] Changes);

/// <summary>
/// The version history shown when a member clicks the version in the footer. Add a release at the top of
/// <see cref="All"/> whenever <c>&lt;ObrlVersion&gt;</c> changes or a notable update ships. The text is interface
/// text: add every new title and change to each <c>Resources/SharedResource.{language}.resx</c> (the localization
/// test lists any that are missing).
/// </summary>
public static class ReleaseNotes
{
    public static readonly IReadOnlyList<Release> All =
    [
        new("1.1", new DateOnly(2026, 11, 5), "Book limits and cover cropping",
        [
            "Users and Contributors can be subscribed to up to 5 books at a time. Moderators and Admins have no limit.",
            "Book subscriptions last 30 days, and Admins can change the length under Admin > Settings. Contributors read the books they uploaded with no subscription and no end date.",
            "Category subscriptions now mean following: the category's new books show on the dashboard, and each book is subscribed to on its own.",
            "Crop and adjust a cover before saving it: zoom, turn, straighten, mirror, brightness, contrast and colour. Works for your own image and for a page of the book.",
            "This version history, opened from the version number in the footer.",
            "The library folder can live on another server (an NFS or SMB share), with the page cache kept on the local disk.",
        ]),
        new("1.0", new DateOnly(2026, 11, 4), "Covers and categories in other languages",
        [
            "Uploaders can add their own cover image, or pick a page of the book as the cover.",
            "Category names and descriptions can be entered in Telugu and Kannada as well as English.",
        ]),
        new("1.0", new DateOnly(2026, 11, 4), "Book details in other languages and a footer",
        [
            "The title, author and description of a book can be entered in every interface language. Search matches all of them.",
            "A footer on every page with the copyright, Terms & Conditions and the version.",
        ]),
        new("1.0", new DateOnly(2026, 11, 3), "English, Telugu and Kannada",
        [
            "The whole interface is available in English, Telugu and Kannada. The choice is saved on the account.",
        ]),
        new("1.0", new DateOnly(2026, 11, 3), "Fewer sign-outs and an idle time setting",
        [
            "Members are no longer signed out while reading.",
            "Admins choose how long members can be inactive before they are signed out, and a notice comes one minute before.",
        ]),
        new("1.0", new DateOnly(2026, 11, 2), "Passkeys",
        [
            "Sign in with a passkey (fingerprint, face or device PIN) instead of a password and code.",
        ]),
        new("1.0", new DateOnly(2026, 11, 2), "Message board, ratings, comments and search",
        [
            "Books from Contributors wait for a Moderator's approval.",
            "Ratings from 1 to 5 stars, comments with replies, and reporting.",
            "A message board, the Most read and Top rated shelves, and full-text search.",
        ]),
        new("1.0", new DateOnly(2026, 11, 2), "First release",
        [
            "A login-only library with nested categories, subscriptions and uploads in PDF, Word, EPUB, text and more.",
            "A reader that never sends the book file: watermarked page pictures for PDF and Word, reflowing text for EPUB and text files.",
            "Required strong sign-in, one session per account, a disclaimer to accept, and an activity history.",
        ]),
    ];

    /// <summary>Every title and change, for the localization test.</summary>
    public static IEnumerable<string> Text => All.SelectMany(r => r.Changes.Prepend(r.Title)).Distinct();
}
