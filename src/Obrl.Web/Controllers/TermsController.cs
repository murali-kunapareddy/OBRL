using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Obrl.Web.Data;

namespace Obrl.Web.Controllers;

/// <summary>
/// Terms &amp; Conditions, linked from every page's footer. It shows the current disclaimer, which is the text members
/// accept before reading (Admins edit it under Admin, Disclaimer). Open to everyone, also before signing in.
/// </summary>
[AllowAnonymous]
public class TermsController(ObrlDbContext db) : Controller
{
    [HttpGet("/terms")]
    public async Task<IActionResult> Index(CancellationToken ct) =>
        View(await db.Disclaimers.AsNoTracking().OrderByDescending(d => d.Version).FirstOrDefaultAsync(ct));
}
