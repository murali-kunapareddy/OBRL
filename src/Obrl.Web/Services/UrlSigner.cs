using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Obrl.Web.Services;

/// <summary>
/// Signs reader URLs so they only work for one user, one book, one page and a short time.
/// A copied link is useless to anyone else and stops working after a minute.
/// </summary>
public sealed class UrlSigner(FileEncryptor encryptor, TimeProvider clock)
{
    private readonly byte[] _key = encryptor.DeriveKey("obrl-reader-url-v1");

    public (long Expires, string Signature) Sign(string userId, Guid bookId, string resource, TimeSpan lifetime)
    {
        var expires = clock.GetUtcNow().Add(lifetime).ToUnixTimeSeconds();
        return (expires, Compute(userId, bookId, resource, expires));
    }

    public bool Verify(string userId, Guid bookId, string resource, long expires, string? signature)
    {
        if (string.IsNullOrEmpty(signature)) return false;
        if (clock.GetUtcNow().ToUnixTimeSeconds() > expires) return false;
        var expected = Encoding.ASCII.GetBytes(Compute(userId, bookId, resource, expires));
        var actual = Encoding.ASCII.GetBytes(signature);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private string Compute(string userId, Guid bookId, string resource, long expires)
    {
        var payload = string.Join('|', userId, bookId.ToString("N"), resource, expires.ToString(CultureInfo.InvariantCulture));
        return WebEncoders.Base64UrlEncode(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload)));
    }
}
