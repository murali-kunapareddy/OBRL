using System.Globalization;
using System.Reflection;

namespace Obrl.Web.Services;

/// <summary>
/// The version shown in the footer: major.minor.build, e.g. "1.0.126D67E87988". major.minor comes from
/// &lt;ObrlVersion&gt; in Obrl.Web.csproj; build is the build time (UTC) written as yyyyMMddHHmmss and converted
/// to hexadecimal, so 2026-11-04 02:39:44 → 20261104023944 → 126D67E87988. Converting the hex back to
/// decimal gives the build time again.
/// </summary>
public static class BuildInfo
{
    private static readonly Dictionary<string, string?> Metadata = typeof(BuildInfo).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .GroupBy(a => a.Key).ToDictionary(g => g.Key, g => g.First().Value);

    public static string MajorMinor { get; } = Metadata.GetValueOrDefault("ObrlVersion") ?? "0.0";

    /// <summary>When the app was built (UTC), or null if the build did not record it.</summary>
    public static DateTime? BuiltAt { get; } =
        DateTime.TryParseExact(Metadata.GetValueOrDefault("ObrlBuildTimestamp"), "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at) ? at : null;

    public static string BuildNumber { get; } = BuiltAt is DateTime t ? ToBuildNumber(t) : "0";

    public static string Version { get; } = $"{MajorMinor}.{BuildNumber}";

    /// <summary>yyyyMMddHHmmss read as a decimal number, written in upper-case hexadecimal.</summary>
    public static string ToBuildNumber(DateTime utc) =>
        long.Parse(utc.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture).ToString("X", CultureInfo.InvariantCulture);
}
