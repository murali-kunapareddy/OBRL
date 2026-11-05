using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Obrl.Web.Data;
using Obrl.Web.Domain;

namespace Obrl.Web.Services;

/// <summary>
/// How many books a member can hold at once, and for how long.
/// <list type="bullet">
/// <item>Users and Contributors can have at most <see cref="MaxBooks"/> active book subscriptions
/// (<c>Obrl:MaxSubscribedBooks</c>, default 5). Unsubscribing, or a subscription reaching its end date, frees a slot.</item>
/// <item>Each new book subscription of a User or Contributor lasts <see cref="LoanDays"/> days, which Admins pick under
/// Admin &gt; Settings (0 means no end date). Changing it affects new subscriptions only.</item>
/// <item>Books a Contributor uploaded never need a subscription, never end and never use a slot.</item>
/// <item>Moderators and Admins read every book, so neither limit applies to them.</item>
/// </list>
/// </summary>
public sealed class Lending(IOptions<ObrlOptions> options, TimeProvider clock)
{
    public const string LoanDaysSettingKey = "LoanDays";
    public const int DefaultLoanDays = 30;
    public static readonly int[] LoanDayChoices = [0, 7, 14, 30, 60, 90];

    private volatile int _loanDays = DefaultLoanDays;

    public int MaxBooks => Math.Max(1, options.Value.MaxSubscribedBooks);

    /// <summary>Days a new book subscription lasts; 0 means no end date.</summary>
    public int LoanDays => _loanDays;

    public static bool IsAllowedLoan(int days) => LoanDayChoices.Contains(days);

    public static bool IsLimited(ClaimsPrincipal user) => !AccessService.ReadsEverything(user);

    public async Task LoadAsync(ObrlDbContext db)
    {
        var stored = await db.SiteSettings.AsNoTracking().Where(s => s.Key == LoanDaysSettingKey).Select(s => s.Value).FirstOrDefaultAsync();
        _loanDays = int.TryParse(stored, CultureInfo.InvariantCulture, out var d) && d >= 0 ? d : DefaultLoanDays;
    }

    public async Task SaveAsync(ObrlDbContext db, int days, string adminId)
    {
        if (!IsAllowedLoan(days)) throw new ArgumentOutOfRangeException(nameof(days));
        var row = await db.SiteSettings.FirstOrDefaultAsync(s => s.Key == LoanDaysSettingKey);
        if (row is null) db.SiteSettings.Add(row = new SiteSetting { Key = LoanDaysSettingKey });
        row.Value = days.ToString(CultureInfo.InvariantCulture);
        row.UpdatedAt = clock.GetUtcNow();
        row.UpdatedById = adminId;
        await db.SaveChangesAsync();
        _loanDays = days;
    }

    /// <summary>Slots the member is using right now.</summary>
    public Task<int> SlotsInUseAsync(ObrlDbContext db, string userId, CancellationToken ct) =>
        AccessService.SlotsInUse(db, userId, clock.GetUtcNow()).CountAsync(ct);

    /// <summary>
    /// Subscribes the member to a book, or renews an ended subscription. Returns what happened.
    /// For Postgres the check and the insert run under a per-member lock, so two quick clicks cannot pass the limit.
    /// </summary>
    public async Task<SubscribeResult> SubscribeAsync(ObrlDbContext db, ClaimsPrincipal user, Guid bookId, CancellationToken ct)
    {
        var userId = AccessService.UserId(user);
        var now = clock.GetUtcNow();
        var limited = IsLimited(user);

        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({"obrl-sub:" + userId}))", ct);

        var book = await db.Books.Where(b => b.Id == bookId).Select(b => new { b.UploadedById }).FirstAsync(ct);
        var ownBook = book.UploadedById == userId;
        var existing = await db.Subscriptions.FirstOrDefaultAsync(s => s.UserId == userId && s.BookId == bookId, ct);
        if (existing is not null && (existing.ExpiresAt is null || existing.ExpiresAt > now))
            return SubscribeResult.AlreadySubscribed;

        if (limited && !ownBook && await AccessService.SlotsInUse(db, userId, now).CountAsync(ct) >= MaxBooks)
            return SubscribeResult.LimitReached;

        var expires = limited && !ownBook && LoanDays > 0 ? now.AddDays(LoanDays) : (DateTimeOffset?)null;
        if (existing is null)
        {
            db.Subscriptions.Add(new Subscription { UserId = userId, BookId = bookId, CreatedAt = now, ExpiresAt = expires });
        }
        else
        {
            existing.CreatedAt = now;
            existing.ExpiresAt = expires;
        }
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return SubscribeResult.Subscribed;
    }
}

public enum SubscribeResult
{
    Subscribed,
    AlreadySubscribed,
    LimitReached,
}
