using Microsoft.Extensions.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Obrl.Web.Data;
using Obrl.Web.Domain;
using Obrl.Web.Models;
using Obrl.Web.Security;
using Obrl.Web.Services;
using Obrl.Web.Services.Email;

namespace Obrl.Web.Controllers;

/// <summary>Admins manage accounts, roles and the disclaimer, and can look at recent activity.</summary>
[Authorize(Policy = Policies.AdminOnly)]
public class AdminController(
    ObrlDbContext db,
    UserManager<ApplicationUser> users,
    ActivityLog log,
    IMemoryCache cache,
    IdleTimeout idle,
    Lending lending,
    EmailSettings emailSettings,
    Mailer mailer,
    Notifications notify,
    IStringLocalizer<SharedResource> L) : Controller
{
    public async Task<IActionResult> Users(string? q, CancellationToken ct)
    {
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = "%" + q.Trim() + "%";
            query = query.Where(u => EF.Functions.ILike(u.Email!, pattern) || EF.Functions.ILike(u.DisplayName, pattern));
        }
        var list = await query.OrderBy(u => u.Email).Take(500).ToListAsync(ct);
        var roleByUser = await (from ur in db.UserRoles
                                join r in db.Roles on ur.RoleId equals r.Id
                                select new { ur.UserId, r.Name }).ToListAsync(ct);
        var passkeys = await db.UserPasskeys.GroupBy(p => p.UserId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var now = DateTimeOffset.UtcNow;
        ViewBag.Query = q;
        return View(list.Select(u => new UserRow(
            u.Id, u.Email ?? "", u.DisplayName,
            roleByUser.FirstOrDefault(r => r.UserId == u.Id)?.Name ?? Roles.User,
            u.TwoFactorEnabled, passkeys.GetValueOrDefault(u.Id), u.LockoutEnd > now, u.CreatedAt, u.LastLoginAt)).ToList());
    }

    [HttpGet]
    public IActionResult CreateUser()
    {
        ViewBag.CanEmail = notify.CanWelcomeUsers;
        return View(new CreateUserVm { SendEmail = notify.CanWelcomeUsers });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateUser(CreateUserVm vm, CancellationToken ct)
    {
        var sendEmail = vm.SendEmail && notify.CanWelcomeUsers;
        if (!Roles.All.Contains(vm.Role)) ModelState.AddModelError(nameof(vm.Role), L["Unknown role."]);
        if (string.IsNullOrWhiteSpace(vm.Password) && !sendEmail)
            ModelState.AddModelError(nameof(vm.Password), L["Enter a temporary password, or have it emailed."]);
        if (!ModelState.IsValid)
        {
            ViewBag.CanEmail = notify.CanWelcomeUsers;
            return View(vm);
        }

        // An empty password with email on: generate one, so nobody but the new member ever sees it.
        var generated = string.IsNullOrWhiteSpace(vm.Password);
        var password = generated ? Notifications.NewTemporaryPassword() : vm.Password!;
        var user = new ApplicationUser
        {
            UserName = vm.Email.Trim(),
            Email = vm.Email.Trim(),
            EmailConfirmed = true,
            DisplayName = vm.DisplayName.Trim(),
            MustChangePassword = true,
        };
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError("", L[e.Description]);
            ViewBag.CanEmail = notify.CanWelcomeUsers;
            return View(vm);
        }
        await users.AddToRoleAsync(user, vm.Role);
        var adminId = AccessService.UserId(User);
        await log.WriteAsync(ActivityTypes.UserCreated, adminId, data: new { user.Id, user.Email, vm.Role });
        TempData["Message"] = await SendSignInDetailsAsync(user, password, generated, sendEmail, isReset: false, ct);
        return RedirectToAction(nameof(Users));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetRole(string id, string role)
    {
        if (!Roles.All.Contains(role)) return BadRequest();
        if (id == AccessService.UserId(User)) return Back(L["You cannot change your own role."]);
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        await users.RemoveFromRolesAsync(user, await users.GetRolesAsync(user));
        await users.AddToRoleAsync(user, role);
        await users.UpdateSecurityStampAsync(user); // their session picks up the new role
        await log.WriteAsync(ActivityTypes.RoleChanged, AccessService.UserId(User), data: new { user.Id, role });
        return Back(L["{0} is now {1}.", user.Email!, L[role]]);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Lock(string id, bool locked)
    {
        if (id == AccessService.UserId(User)) return Back(L["You cannot lock your own account."]);
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        await users.SetLockoutEnabledAsync(user, true);
        await users.SetLockoutEndDateAsync(user, locked ? DateTimeOffset.MaxValue : null);
        if (!locked) await users.ResetAccessFailedCountAsync(user);
        return Back(locked ? L["{0} is locked out.", user.Email!] : L["{0} is unlocked.", user.Email!]);
    }

    /// <summary>
    /// Gives a member a new temporary password, typed by the Admin or (left empty, with email on) generated and emailed.
    /// The member must choose their own password at their next sign-in.
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(string id, string? password, bool sendEmail, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        sendEmail = sendEmail && notify.CanWelcomeUsers;
        var generated = string.IsNullOrWhiteSpace(password);
        if (generated && !sendEmail) return Back(L["Enter a temporary password, or have it emailed."]);
        var newPassword = generated ? Notifications.NewTemporaryPassword() : password!;
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, token, newPassword);
        if (!result.Succeeded) return Back(string.Join(" ", result.Errors.Select(e => L[e.Description].Value)));
        user.MustChangePassword = true;
        await users.UpdateAsync(user);
        return Back(await SendSignInDetailsAsync(user, newPassword, generated, sendEmail, isReset: true, ct));
    }

    /// <summary>Emails the sign-in details when asked to, and says what happened (and the password, when it was not emailed).</summary>
    private async Task<string> SendSignInDetailsAsync(ApplicationUser user, string password, bool generated, bool sendEmail, bool isReset, CancellationToken ct)
    {
        var adminId = AccessService.UserId(User);
        string done = isReset ? L["New temporary password set for {0}.", user.Email!] : L["Created {0}.", user.Email!];
        if (!sendEmail)
            return done + " " + L["Give them the temporary password privately. They choose their own password and set up two-factor sign-in at first login."].Value;
        var error = await notify.WelcomeAsync(user, password, isReset, ct);
        if (error is null)
        {
            await log.WriteAsync(ActivityTypes.EmailSent, adminId, data: new { to = user.Email, kind = isReset ? "password_reset" : "welcome" }, ct: ct);
            return done + " " + L["The sign-in details were emailed to them. They choose their own password and set up two-factor sign-in at first login."].Value;
        }
        await log.WriteAsync(ActivityTypes.EmailFailed, adminId, data: new { to = user.Email, kind = isReset ? "password_reset" : "welcome", error }, ct: ct);
        return done + " " + (generated
            ? L["The email could not be sent ({0}). Give them this temporary password privately: {1}", error, password].Value
            : L["The email could not be sent ({0}). Give them the temporary password privately.", error].Value);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetTwoFactor(string id)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        await users.SetTwoFactorEnabledAsync(user, false);
        await users.ResetAuthenticatorKeyAsync(user);
        // A lost phone often holds the passkey too, so passkeys go as well.
        foreach (var passkey in await users.GetPasskeysAsync(user))
            await users.RemovePasskeyAsync(user, passkey.CredentialId);
        user.CurrentSessionId = null;
        await users.UpdateAsync(user);
        return Back(L["Authenticator app and passkeys reset for {0}. They will set up a strong sign-in again at next login.", user.Email!]);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SignOutUser(string id)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        user.CurrentSessionId = null;
        await users.UpdateAsync(user);
        return Back(L["{0} was signed out.", user.Email!]);
    }

    [HttpGet]
    public IActionResult Settings()
    {
        var m = mailer.Settings;
        return View(new SettingsVm(idle.Minutes, lending.LoanDays, lending.MaxBooks, new EmailStatusVm(
            mailer.IsConfigured, mailer.IsConfigured ? $"{m.Host}:{m.Port}" : null, m.FromAddress,
            emailSettings.NotifyUploads, emailSettings.WelcomeUsers)));
    }

    /// <summary>Which emails go out. The mail server itself is set in the server's configuration.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Email(bool notifyUploads, bool welcomeUsers)
    {
        var adminId = AccessService.UserId(User);
        var before = new { emailSettings.NotifyUploads, emailSettings.WelcomeUsers };
        await emailSettings.SaveAsync(db, notifyUploads, welcomeUsers, adminId);
        await log.WriteAsync(ActivityTypes.SettingChanged, adminId, data: new { setting = "Email", before, after = new { notifyUploads, welcomeUsers } });
        TempData["Message"] = L["Email settings saved."].Value;
        return RedirectToAction(nameof(Settings), null, "email");
    }

    /// <summary>Sends a test email to the signed-in Admin.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> TestEmail(CancellationToken ct)
    {
        var me = await users.GetUserAsync(User);
        if (me?.Email is null) return NotFound();
        var error = await notify.TestAsync(me.Email, me.DisplayName, ct);
        TempData["Message"] = error is null
            ? L["A test email was sent to {0}. Check the inbox (and the spam folder).", me.Email].Value
            : L["The test email could not be sent: {0}", error].Value;
        return RedirectToAction(nameof(Settings), null, "email");
    }

    // ---------- Book lists: the languages and publishers uploaders pick from ----------

    [HttpGet]
    public async Task<IActionResult> BookLists(CancellationToken ct)
    {
        var languageUse = await db.Books.Where(b => b.LanguageId != null).GroupBy(b => b.LanguageId!.Value)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var publisherUse = await db.Books.Where(b => b.PublisherId != null).GroupBy(b => b.PublisherId!.Value)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return View(new BookListsVm
        {
            Languages = (await db.BookLanguages.AsNoTracking().OrderBy(l => l.SortOrder).ThenBy(l => l.Name).ToListAsync(ct))
                .Select(l => new BookListRow(l.Id, l.Name, l.SortOrder, languageUse.GetValueOrDefault(l.Id))).ToList(),
            Publishers = (await db.Publishers.AsNoTracking().Include(p => p.Translations).OrderBy(p => p.Name).ToListAsync(ct))
                .Select(p => new BookListRow(p.Id, p.Name, 0, publisherUse.GetValueOrDefault(p.Id))
                {
                    Names = p.Translations.ToDictionary(t => t.Language, t => t.Name),
                }).ToList(),
        });
    }

    [HttpGet]
    public async Task<IActionResult> EditLanguage(int? id, CancellationToken ct)
    {
        if (id is null) return View(new BookLanguageEditVm { SortOrder = (await db.BookLanguages.MaxAsync(l => (int?)l.SortOrder, ct) ?? 0) + 1 });
        var language = await db.BookLanguages.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);
        if (language is null) return NotFound();
        return View(new BookLanguageEditVm
        {
            Id = language.Id, Name = language.Name, SortOrder = language.SortOrder,
            Books = await db.Books.CountAsync(b => b.LanguageId == id, ct),
        });
    }

    /// <summary>Adds a book language, or renames one and changes its place in the list.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditLanguage(BookLanguageEditVm vm, CancellationToken ct)
    {
        var clean = CleanName(vm.Name, 60);
        if (clean is null) ModelState.AddModelError(nameof(vm.Name), L["Enter a language name of up to {0} characters.", 60]);
        var normalized = clean?.ToUpperInvariant();
        if (clean is not null && await db.BookLanguages.AnyAsync(l => l.NormalizedName == normalized && l.Id != vm.Id, ct))
            ModelState.AddModelError(nameof(vm.Name), L["“{0}” is already on the list.", clean]);
        BookLanguage? language = null;
        if (vm.Id is int id)
        {
            language = await db.BookLanguages.FirstOrDefaultAsync(l => l.Id == id, ct);
            if (language is null) return NotFound();
        }
        if (!ModelState.IsValid)
        {
            vm.Books = vm.Id is int used ? await db.Books.CountAsync(b => b.LanguageId == used, ct) : 0;
            return View(vm);
        }
        if (language is null) db.BookLanguages.Add(language = new BookLanguage());
        language.Name = clean!;
        language.NormalizedName = normalized!;
        language.SortOrder = Math.Max(0, vm.SortOrder);
        await db.SaveChangesAsync(ct);
        return Back(L["Saved."], nameof(BookLists));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteLanguage(int id, CancellationToken ct)
    {
        var language = await db.BookLanguages.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (language is null) return NotFound();
        var used = await db.Books.CountAsync(b => b.LanguageId == id, ct);
        if (used > 0) return Back(L["“{0}” is used by {1} books. Change their language first.", language.Name, used], nameof(BookLists));
        db.BookLanguages.Remove(language);
        await db.SaveChangesAsync(ct);
        return Back(L["“{0}” was deleted.", language.Name], nameof(BookLists));
    }

    [HttpGet]
    public async Task<IActionResult> EditPublisher(int? id, CancellationToken ct)
    {
        var vm = new PublisherEditVm();
        if (id is not null)
        {
            var publisher = await db.Publishers.AsNoTracking().Include(p => p.Translations).FirstOrDefaultAsync(p => p.Id == id, ct);
            if (publisher is null) return NotFound();
            vm.Id = publisher.Id;
            vm.Name = publisher.Name;
            vm.Books = await db.Books.CountAsync(b => b.PublisherId == id, ct);
            vm.FillTranslations(publisher.Translations);
        }
        else vm.FillTranslations([]);
        return View(vm);
    }

    /// <summary>
    /// Adds a publisher, or renames one and sets its name in other languages. Renaming it to another publisher's name
    /// moves its books to that publisher and removes this entry.
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditPublisher(PublisherEditVm vm, CancellationToken ct)
    {
        var clean = CleanName(vm.Name, 200);
        if (clean is null) ModelState.AddModelError(nameof(vm.Name), L["Enter a publisher name of up to {0} characters.", 200]);
        Publisher? publisher = null;
        if (vm.Id is int id)
        {
            publisher = await db.Publishers.Include(p => p.Translations).FirstOrDefaultAsync(p => p.Id == id, ct);
            if (publisher is null) return NotFound();
        }
        var normalized = clean is null ? null : Publisher.Normalize(clean);
        var twin = normalized is null ? null
            : await db.Publishers.Include(p => p.Translations).FirstOrDefaultAsync(p => p.NormalizedName == normalized && p.Id != vm.Id, ct);
        if (twin is not null && publisher is null)
            ModelState.AddModelError(nameof(vm.Name), L["“{0}” is already on the list.", clean!]);
        if (!ModelState.IsValid)
        {
            vm.Books = vm.Id is int used ? await db.Books.CountAsync(b => b.PublisherId == used, ct) : 0;
            vm.FillTranslations(publisher?.Translations ?? []);
            return View(vm);
        }

        var names = vm.Translations.Select(t => KeyValuePair.Create(t.Language, t.Name));
        if (twin is not null && publisher is not null)
        {
            // Same publisher typed two ways: keep the other entry, move this one's books to it.
            await db.Books.Where(b => b.PublisherId == publisher.Id).ExecuteUpdateAsync(u => u.SetProperty(b => b.PublisherId, twin.Id), ct);
            db.Publishers.Remove(publisher);
            twin.Name = clean!;
            PublisherText.Apply(twin, names, emptyRemoves: false);
            await db.SaveChangesAsync(ct);
            return Back(L["Merged into “{0}”.", clean!], nameof(BookLists));
        }
        if (publisher is null) db.Publishers.Add(publisher = new Publisher());
        publisher.Name = clean!;
        publisher.NormalizedName = normalized!;
        PublisherText.Apply(publisher, names, emptyRemoves: true);
        await db.SaveChangesAsync(ct);
        return Back(L["Saved."], nameof(BookLists));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePublisher(int id, CancellationToken ct)
    {
        var publisher = await db.Publishers.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (publisher is null) return NotFound();
        var used = await db.Books.CountAsync(b => b.PublisherId == id, ct);
        if (used > 0) return Back(L["“{0}” is used by {1} books. Change their publisher first.", publisher.Name, used], nameof(BookLists));
        db.Publishers.Remove(publisher);
        await db.SaveChangesAsync(ct);
        return Back(L["“{0}” was deleted.", publisher.Name], nameof(BookLists));
    }

    private static string? CleanName(string? name, int max)
    {
        var clean = string.Join(' ', (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length > 0 && clean.Length <= max ? clean : null;
    }

    /// <summary>How long a new book subscription of a User or Contributor lasts (0 = no end date).</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Loans(int loanDays)
    {
        if (!Lending.IsAllowedLoan(loanDays)) return BadRequest();
        var before = lending.LoanDays;
        var adminId = AccessService.UserId(User);
        await lending.SaveAsync(db, loanDays, adminId);
        await log.WriteAsync(ActivityTypes.SettingChanged, adminId, data: new { setting = Lending.LoanDaysSettingKey, before, after = loanDays });
        TempData["Message"] = (loanDays == 0
            ? L["New book subscriptions now have no end date."]
            : L["New book subscriptions now last {0} days.", loanDays]).Value;
        return RedirectToAction(nameof(Settings));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Settings(int idleMinutes)
    {
        if (!IdleTimeout.IsAllowed(idleMinutes)) return BadRequest();
        var before = idle.Minutes;
        var adminId = AccessService.UserId(User);
        await idle.SaveAsync(db, idleMinutes, adminId);
        await log.WriteAsync(ActivityTypes.SettingChanged, adminId, data: new { setting = IdleTimeout.SettingKey, before, after = idleMinutes });
        TempData["Message"] = L["Members are now signed out after {0} minutes without activity.", idleMinutes].Value;
        return RedirectToAction(nameof(Settings));
    }

    [HttpGet]
    public async Task<IActionResult> Disclaimer(CancellationToken ct) =>
        View(await db.Disclaimers.AsNoTracking().OrderByDescending(d => d.Version).FirstOrDefaultAsync(ct));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Disclaimer(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return Back(L["The disclaimer cannot be empty."], nameof(Disclaimer));
        var latest = await db.Disclaimers.OrderByDescending(d => d.Version).FirstOrDefaultAsync(ct);
        if (latest?.Text.Trim() == text.Trim()) return Back(L["No changes."], nameof(Disclaimer));
        db.Disclaimers.Add(new Disclaimer
        {
            Version = (latest?.Version ?? 0) + 1,
            Text = text.Trim(),
            CreatedById = AccessService.UserId(User),
        });
        await db.SaveChangesAsync(ct);
        cache.Remove(SessionRules.DisclaimerCacheKey);
        return Back(L["Saved. Every user must accept the new version at their next page view."], nameof(Disclaimer));
    }

    public async Task<IActionResult> Activity(string? type, CancellationToken ct)
    {
        var q = db.ActivityEvents.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(type)) q = q.Where(e => e.Type == type);
        var events = await q.OrderByDescending(e => e.Id).Take(300).ToListAsync(ct);
        var userIds = events.Select(e => e.UserId).Where(id => id != null).Distinct().ToList();
        ViewBag.Emails = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email, ct);
        var bookIds = events.Select(e => e.BookId).Where(id => id != null).Distinct().ToList();
        ViewBag.Titles = await db.Books.Where(b => bookIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Title, ct);
        ViewBag.Type = type;
        return View(events);
    }

    private IActionResult Back(string message, string action = nameof(Users))
    {
        TempData["Message"] = message;
        return RedirectToAction(action);
    }
}
