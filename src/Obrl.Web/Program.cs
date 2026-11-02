using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Obrl.Web.Data;
using Obrl.Web.Domain;
using Obrl.Web.Security;
using Obrl.Web.Services;
using Obrl.Web.Services.Ingest;
using Obrl.Web.Services.Rendering;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---------- Options and core services ----------
builder.Services.Configure<ObrlOptions>(config.GetSection(ObrlOptions.Section));
var obrl = config.GetSection(ObrlOptions.Section).Get<ObrlOptions>() ?? new ObrlOptions();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp => new FileEncryptor(
    FileEncryptor.LoadOrCreateKey(obrl.KeyFile, sp.GetRequiredService<ILoggerFactory>().CreateLogger("Keys"))));
builder.Services.AddSingleton<BookStorage>();
builder.Services.AddSingleton<UrlSigner>();
builder.Services.AddSingleton<ExternalTools>();
builder.Services.AddSingleton<PageRenderer>();
builder.Services.AddScoped<BookProcessor>();
builder.Services.AddScoped<AccessService>();
builder.Services.AddScoped<ActivityLog>();
builder.Services.AddHostedService<ConversionWorker>();
builder.Services.AddHttpContextAccessor();

// Keys that protect sign-in cookies and 2FA tokens live with the rest of OBRL's data, so they survive restarts.
builder.Services.AddDataProtection()
    .SetApplicationName("OBRL")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Path.GetFullPath(obrl.StorageRoot), "keys")));
builder.Services.AddMemoryCache();

// ---------- Database ----------
builder.Services.AddDbContext<ObrlDbContext>(o =>
    o.UseNpgsql(config.GetConnectionString("Default")));

// ---------- Accounts ----------
builder.Services
    .AddDefaultIdentity<ApplicationUser>(o =>
    {
        // Length beats complexity rules (NIST 800-63B); common passwords are rejected separately.
        o.Password.RequiredLength = 12;
        o.Password.RequiredUniqueChars = 4;
        o.Password.RequireDigit = false;
        o.Password.RequireLowercase = false;
        o.Password.RequireUppercase = false;
        o.Password.RequireNonAlphanumeric = false;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        o.Lockout.AllowedForNewUsers = true;
        o.User.RequireUniqueEmail = true;
        o.SignIn.RequireConfirmedAccount = false;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ObrlDbContext>()
    .AddSignInManager<ObrlSignInManager>()
    .AddPasswordValidator<CommonPasswordValidator>();

builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(5));
builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "obrl.auth";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromMinutes(obrl.IdleTimeoutMinutes);
    o.SlidingExpiration = true;
    o.LoginPath = "/Identity/Account/Login";
    o.AccessDeniedPath = "/Identity/Account/AccessDenied";
    o.Events.OnSigningIn = SessionRules.OnSigningIn;
});
builder.Services.AddTransient<IEmailSender, LogOnlyEmailSender>();

builder.Services.AddAuthorizationBuilder()
    // OBRL is login-only: every page needs a signed-in user unless it says otherwise.
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy(Policies.CanContribute, p => p.RequireRole(Roles.Contributor, Roles.Moderator, Roles.Admin))
    .AddPolicy(Policies.CanModerate, p => p.RequireRole(Roles.Moderator, Roles.Admin))
    .AddPolicy(Policies.AdminOnly, p => p.RequireRole(Roles.Admin));

builder.Services.AddAntiforgery(o => o.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

// ---------- Uploads ----------
var maxUpload = obrl.MaxUploadMegabytes * 1024L * 1024L;
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = maxUpload);
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = maxUpload + 1024 * 1024);

// ---------- Reader rate limits ----------
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("reader", http =>
    {
        var key = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? http.Connection.RemoteIpAddress?.ToString() ?? "anon";
        return RateLimitPartition.Get(key, _ => RateLimiter.CreateChained(
            new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
            {
                PermitLimit = obrl.ReaderRequestsPerMinute,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueLimit = 0,
            }),
            new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
            {
                PermitLimit = obrl.ReaderRequestsPerHour,
                Window = TimeSpan.FromHours(1),
                SegmentsPerWindow = 12,
                QueueLimit = 0,
            })));
    });
    // Comments, ratings, reports and board posts: enough for a lively discussion, too few for spam.
    o.AddPolicy("posting", http =>
    {
        var key = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? http.Connection.RemoteIpAddress?.ToString() ?? "anon";
        return RateLimitPartition.Get(key, _ => RateLimiter.CreateChained(
            new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueLimit = 0,
            }),
            new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromHours(1),
                SegmentsPerWindow = 12,
                QueueLimit = 0,
            })));
    });
    o.OnRejected = async (ctx, ct) =>
    {
        var userId = ctx.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        await ctx.HttpContext.RequestServices.GetRequiredService<ActivityLog>()
            .WriteAsync(ActivityTypes.RateLimited, userId, data: new { path = ctx.HttpContext.Request.Path.Value }, ct: ct);
    };
});

builder.Services.Configure<ForwardedHeadersOptions>(o =>
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);

var app = builder.Build();

await Seeder.RunAsync(app.Services);

// ---------- Pipeline ----------
app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.Use(async (http, next) =>
{
    var h = http.Response.Headers;
    h.XContentTypeOptions = "nosniff";
    h.XFrameOptions = "DENY";
    h["Referrer-Policy"] = "same-origin";
    h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    h.ContentSecurityPolicy =
        "default-src 'self'; img-src 'self' blob: data:; style-src 'self' 'unsafe-inline'; script-src 'self'; " +
        "font-src 'self'; connect-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    await next();
});

// Accounts are created by admins only: Identity's self-service registration and account deletion are switched off.
string[] blockedIdentityPages =
[
    "/Identity/Account/Register", "/Identity/Account/RegisterConfirmation", "/Identity/Account/ExternalLogin",
    "/Identity/Account/ResendEmailConfirmation", "/Identity/Account/ForgotPassword", "/Identity/Account/Manage/DeletePersonalData",
    "/Identity/Account/Manage/ExternalLogins", "/Identity/Account/Manage/Disable2fa",
];
app.Use(async (http, next) =>
{
    if (blockedIdentityPages.Any(p => http.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
    {
        http.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    await next();
});

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.Use(SessionRules.Enforce);
app.UseAuthorization();
app.UseRateLimiter();

app.MapStaticAssets().AllowAnonymous();
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}").WithStaticAssets();
app.MapRazorPages().WithStaticAssets();

app.Run();

public partial class Program;
