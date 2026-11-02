namespace Obrl.Web.Domain;

/// <summary>
/// The four OBRL roles. Each role includes the rights of the ones above it in this list.
/// A user holds exactly one role.
/// </summary>
public static class Roles
{
    public const string User = "User";
    public const string Contributor = "Contributor";
    public const string Moderator = "Moderator";
    public const string Admin = "Admin";

    public static readonly string[] All = [User, Contributor, Moderator, Admin];
}

public static class Policies
{
    /// <summary>Contributor, Moderator or Admin: may upload books.</summary>
    public const string CanContribute = nameof(CanContribute);

    /// <summary>Moderator or Admin: manages categories and all books.</summary>
    public const string CanModerate = nameof(CanModerate);

    /// <summary>Admin only: users, roles, disclaimer.</summary>
    public const string AdminOnly = nameof(AdminOnly);
}
