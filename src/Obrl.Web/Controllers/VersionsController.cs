using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Obrl.Web.Controllers;

/// <summary>Version history, opened from the version number in every page's footer. Open to everyone, like Terms &amp; Conditions.</summary>
[AllowAnonymous]
public class VersionsController : Controller
{
    [HttpGet("/versions")]
    public IActionResult Index() => View(Services.ReleaseNotes.All);
}
