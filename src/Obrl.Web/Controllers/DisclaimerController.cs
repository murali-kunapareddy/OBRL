using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Obrl.Web.Data;
using Obrl.Web.Domain;
using Obrl.Web.Models;
using Obrl.Web.Services;

namespace Obrl.Web.Controllers;

/// <summary>Every user must accept the latest disclaimer before using OBRL.</summary>
public class DisclaimerController(ObrlDbContext db, ActivityLog log) : Controller
{
    public async Task<IActionResult> Index(string? returnUrl, CancellationToken ct)
    {
        var latest = await db.Disclaimers.AsNoTracking().OrderByDescending(d => d.Version).FirstOrDefaultAsync(ct);
        if (latest is null) return Redirect("/");
        return View(new DisclaimerVm { Disclaimer = latest, ReturnUrl = returnUrl });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Accept(int version, bool agree, string? returnUrl, CancellationToken ct)
    {
        var userId = AccessService.UserId(User);
        var latest = await db.Disclaimers.MaxAsync(d => (int?)d.Version, ct) ?? 0;
        if (!agree || version != latest) return RedirectToAction(nameof(Index), new { returnUrl });

        if (!await db.TermsAcceptances.AnyAsync(t => t.UserId == userId && t.DisclaimerVersion == version, ct))
        {
            db.TermsAcceptances.Add(new TermsAcceptance
            {
                UserId = userId,
                DisclaimerVersion = version,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            });
            log.Add(ActivityTypes.DisclaimerAccepted, userId, data: new { version });
            await db.SaveChangesAsync(ct);
        }
        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : Redirect("/");
    }
}
