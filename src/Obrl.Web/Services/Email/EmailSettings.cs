using Microsoft.EntityFrameworkCore;
using Obrl.Web.Data;
using Obrl.Web.Domain;

namespace Obrl.Web.Services.Email;

/// <summary>
/// Which emails OBRL sends, chosen by Admins under Admin &gt; Settings. Both are on until an Admin turns them off,
/// and neither goes out while the mail server is not set up (<see cref="ObrlOptions.Email"/>).
/// </summary>
public sealed class EmailSettings(TimeProvider clock)
{
    public const string NotifyUploadsKey = "EmailNotifyUploads";
    public const string WelcomeUsersKey = "EmailWelcomeUsers";

    private volatile bool _notifyUploads = true;
    private volatile bool _welcomeUsers = true;

    /// <summary>Email Moderators and Admins when a book is uploaded.</summary>
    public bool NotifyUploads => _notifyUploads;

    /// <summary>Email new members (and members given a new temporary password) their sign-in details.</summary>
    public bool WelcomeUsers => _welcomeUsers;

    public async Task LoadAsync(ObrlDbContext db)
    {
        var rows = await db.SiteSettings.AsNoTracking()
            .Where(s => s.Key == NotifyUploadsKey || s.Key == WelcomeUsersKey)
            .ToDictionaryAsync(s => s.Key, s => s.Value);
        _notifyUploads = !rows.TryGetValue(NotifyUploadsKey, out var n) || n != "false";
        _welcomeUsers = !rows.TryGetValue(WelcomeUsersKey, out var w) || w != "false";
    }

    public async Task SaveAsync(ObrlDbContext db, bool notifyUploads, bool welcomeUsers, string adminId)
    {
        await SetAsync(db, NotifyUploadsKey, notifyUploads, adminId);
        await SetAsync(db, WelcomeUsersKey, welcomeUsers, adminId);
        await db.SaveChangesAsync();
        _notifyUploads = notifyUploads;
        _welcomeUsers = welcomeUsers;
    }

    private async Task SetAsync(ObrlDbContext db, string key, bool value, string adminId)
    {
        var row = await db.SiteSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (row is null) db.SiteSettings.Add(row = new SiteSetting { Key = key });
        row.Value = value ? "true" : "false";
        row.UpdatedAt = clock.GetUtcNow();
        row.UpdatedById = adminId;
    }
}
