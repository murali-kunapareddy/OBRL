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

namespace Obrl.Web.Controllers;

/// <summary>Admins manage accounts, roles and the disclaimer, and can look at recent activity.</summary>
[Authorize(Policy = Policies.AdminOnly)]
public class AdminController(
    ObrlDbContext db,
    UserManager<ApplicationUser> users,
    ActivityLog log,
    IMemoryCache cache,
    IdleTimeout idle) : Controller
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
    public IActionResult CreateUser() => View(new CreateUserVm());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateUser(CreateUserVm vm)
    {
        if (!Roles.All.Contains(vm.Role)) ModelState.AddModelError(nameof(vm.Role), "Unknown role.");
        if (!ModelState.IsValid) return View(vm);

        var user = new ApplicationUser
        {
            UserName = vm.Email.Trim(),
            Email = vm.Email.Trim(),
            EmailConfirmed = true,
            DisplayName = vm.DisplayName.Trim(),
        };
        var result = await users.CreateAsync(user, vm.Password);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError("", e.Description);
            return View(vm);
        }
        await users.AddToRoleAsync(user, vm.Role);
        await log.WriteAsync(ActivityTypes.UserCreated, AccessService.UserId(User), data: new { user.Id, user.Email, vm.Role });
        TempData["Message"] = $"Created {user.Email}. Give them the temporary password; they will set up two-factor sign-in at first login.";
        return RedirectToAction(nameof(Users));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetRole(string id, string role)
    {
        if (!Roles.All.Contains(role)) return BadRequest();
        if (id == AccessService.UserId(User)) return Back("You cannot change your own role.");
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        await users.RemoveFromRolesAsync(user, await users.GetRolesAsync(user));
        await users.AddToRoleAsync(user, role);
        await users.UpdateSecurityStampAsync(user); // their session picks up the new role
        await log.WriteAsync(ActivityTypes.RoleChanged, AccessService.UserId(User), data: new { user.Id, role });
        return Back($"{user.Email} is now {role}.");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Lock(string id, bool locked)
    {
        if (id == AccessService.UserId(User)) return Back("You cannot lock your own account.");
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        await users.SetLockoutEnabledAsync(user, true);
        await users.SetLockoutEndDateAsync(user, locked ? DateTimeOffset.MaxValue : null);
        if (!locked) await users.ResetAccessFailedCountAsync(user);
        return Back(locked ? $"{user.Email} is locked out." : $"{user.Email} is unlocked.");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(string id, string password)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, token, password ?? "");
        return Back(result.Succeeded
            ? $"New temporary password set for {user.Email}."
            : string.Join(" ", result.Errors.Select(e => e.Description)));
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
        return Back($"Authenticator app and passkeys reset for {user.Email}. They will set up a strong sign-in again at next login.");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SignOutUser(string id)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        user.CurrentSessionId = null;
        await users.UpdateAsync(user);
        return Back($"{user.Email} was signed out.");
    }

    [HttpGet]
    public IActionResult Settings() => View(idle.Minutes);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Settings(int idleMinutes)
    {
        if (!IdleTimeout.IsAllowed(idleMinutes)) return BadRequest();
        var before = idle.Minutes;
        var adminId = AccessService.UserId(User);
        await idle.SaveAsync(db, idleMinutes, adminId);
        await log.WriteAsync(ActivityTypes.SettingChanged, adminId, data: new { setting = IdleTimeout.SettingKey, before, after = idleMinutes });
        TempData["Message"] = $"Members are now signed out after {idleMinutes} minutes without activity.";
        return RedirectToAction(nameof(Settings));
    }

    [HttpGet]
    public async Task<IActionResult> Disclaimer(CancellationToken ct) =>
        View(await db.Disclaimers.AsNoTracking().OrderByDescending(d => d.Version).FirstOrDefaultAsync(ct));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Disclaimer(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return Back("The disclaimer cannot be empty.", nameof(Disclaimer));
        var latest = await db.Disclaimers.OrderByDescending(d => d.Version).FirstOrDefaultAsync(ct);
        if (latest?.Text.Trim() == text.Trim()) return Back("No changes.", nameof(Disclaimer));
        db.Disclaimers.Add(new Disclaimer
        {
            Version = (latest?.Version ?? 0) + 1,
            Text = text.Trim(),
            CreatedById = AccessService.UserId(User),
        });
        await db.SaveChangesAsync(ct);
        cache.Remove(SessionRules.DisclaimerCacheKey);
        return Back("Saved. Every user must accept the new version at their next page view.", nameof(Disclaimer));
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
