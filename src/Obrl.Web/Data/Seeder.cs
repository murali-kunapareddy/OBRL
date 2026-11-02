using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Obrl.Web.Domain;
using Obrl.Web.Services;

namespace Obrl.Web.Data;

/// <summary>Applies migrations and creates the roles, the first admin and the first disclaimer.</summary>
public static class Seeder
{
    public const string DefaultDisclaimer = """
        Welcome to OBRL, a private online reading library.

        By continuing you agree that:

        1. The books here are for your personal reading only, inside OBRL.
        2. You will not copy, photograph, record, share or redistribute any part of any book.
        3. Your account is for you alone. Do not share your password or sign-in codes.
        4. Every page you view carries a watermark that identifies your account.
        5. OBRL records your reading activity (books opened, pages, bookmarks, sign-ins) to run the library and to investigate misuse.
        6. Breaking these rules can lead to your account being closed.

        Uploaders confirm they have the right to share each book they add.
        """;

    public static async Task RunAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Seeder");
        var db = sp.GetRequiredService<ObrlDbContext>();
        await db.Database.MigrateAsync();

        var roles = sp.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in Roles.All)
            if (!await roles.RoleExistsAsync(role))
                await roles.CreateAsync(new IdentityRole(role));

        if (!await db.Disclaimers.AnyAsync())
        {
            db.Disclaimers.Add(new Disclaimer { Version = 1, Text = DefaultDisclaimer });
            await db.SaveChangesAsync();
        }

        var seed = sp.GetRequiredService<IOptions<ObrlOptions>>().Value.Seed;
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        if ((await users.GetUsersInRoleAsync(Roles.Admin)).Count > 0) return;

        if (string.IsNullOrWhiteSpace(seed.AdminEmail) || string.IsNullOrWhiteSpace(seed.AdminPassword))
        {
            logger.LogWarning("No admin account exists. Set Obrl__Seed__AdminEmail and Obrl__Seed__AdminPassword and restart to create one.");
            return;
        }

        var admin = await users.FindByEmailAsync(seed.AdminEmail);
        if (admin is null)
        {
            admin = new ApplicationUser
            {
                UserName = seed.AdminEmail,
                Email = seed.AdminEmail,
                EmailConfirmed = true,
                DisplayName = "Administrator",
            };
            var created = await users.CreateAsync(admin, seed.AdminPassword);
            if (!created.Succeeded)
            {
                logger.LogError("Could not create the admin account: {Errors}", string.Join("; ", created.Errors.Select(e => e.Description)));
                return;
            }
        }
        await users.AddToRoleAsync(admin, Roles.Admin);
        logger.LogWarning("Created admin account {Email}. Remove the seed password from the configuration now.", seed.AdminEmail);
    }
}
