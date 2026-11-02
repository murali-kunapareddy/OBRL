using System.Security.Cryptography;
using Microsoft.Extensions.Time.Testing;
using Obrl.Web.Security;
using Obrl.Web.Services;
using Obrl.Web.Services.Rendering;
using SkiaSharp;

namespace Obrl.Tests;

public class FileEncryptorTests
{
    private static readonly FileEncryptor Encryptor = new(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void Round_trips_data()
    {
        var plain = RandomNumberGenerator.GetBytes(10_000);
        var sealedBytes = Encryptor.Encrypt(plain);
        Assert.NotEqual(plain, sealedBytes[^plain.Length..]);
        Assert.Equal(plain, Encryptor.Decrypt(sealedBytes));
    }

    [Fact]
    public void Same_input_encrypts_differently_each_time()
    {
        var plain = "same book"u8.ToArray();
        Assert.NotEqual(Encryptor.Encrypt(plain), Encryptor.Encrypt(plain));
    }

    [Fact]
    public void Tampered_file_is_rejected()
    {
        var sealedBytes = Encryptor.Encrypt("chapter one"u8);
        sealedBytes[^1] ^= 0xFF;
        Assert.ThrowsAny<CryptographicException>(() => Encryptor.Decrypt(sealedBytes));
    }

    [Fact]
    public void Other_key_cannot_decrypt()
    {
        var sealedBytes = Encryptor.Encrypt("chapter one"u8);
        var other = new FileEncryptor(RandomNumberGenerator.GetBytes(32));
        Assert.ThrowsAny<CryptographicException>(() => other.Decrypt(sealedBytes));
    }

    [Fact]
    public void Plain_files_are_rejected()
    {
        Assert.Throws<CryptographicException>(() => Encryptor.Decrypt("%PDF-1.7 not encrypted at all"u8));
    }
}

public class UrlSignerTests
{
    private readonly FakeTimeProvider _clock = new(DateTimeOffset.Parse("2026-11-02T08:00:00Z"));
    private readonly UrlSigner _signer;
    private readonly Guid _book = Guid.NewGuid();

    public UrlSignerTests() => _signer = new UrlSigner(new FileEncryptor(RandomNumberGenerator.GetBytes(32)), _clock);

    [Fact]
    public void Valid_signature_is_accepted()
    {
        var (exp, sig) = _signer.Sign("user-1", _book, "page:3:1200", TimeSpan.FromSeconds(60));
        Assert.True(_signer.Verify("user-1", _book, "page:3:1200", exp, sig));
    }

    [Fact]
    public void Expired_link_is_rejected()
    {
        var (exp, sig) = _signer.Sign("user-1", _book, "page:3:1200", TimeSpan.FromSeconds(60));
        _clock.Advance(TimeSpan.FromSeconds(61));
        Assert.False(_signer.Verify("user-1", _book, "page:3:1200", exp, sig));
    }

    [Theory]
    [InlineData("user-2", "page:3:1200")] // someone else's link
    [InlineData("user-1", "page:4:1200")] // another page
    [InlineData("user-1", "page:3:1600")] // another size
    public void Link_only_works_for_its_own_user_and_page(string user, string resource)
    {
        var (exp, sig) = _signer.Sign("user-1", _book, "page:3:1200", TimeSpan.FromSeconds(60));
        Assert.False(_signer.Verify(user, _book, resource, exp, sig));
    }

    [Fact]
    public void Changing_the_expiry_breaks_the_signature()
    {
        var (exp, sig) = _signer.Sign("user-1", _book, "page:0:800", TimeSpan.FromSeconds(60));
        Assert.False(_signer.Verify("user-1", _book, "page:0:800", exp + 3600, sig));
    }
}

public class CommonPasswordTests
{
    [Theory]
    [InlineData("password")]
    [InlineData("password1234")]
    [InlineData("Qwerty123456!")]
    [InlineData("iloveyou2026")]
    public void Common_passwords_and_simple_variations_are_refused(string password) =>
        Assert.True(CommonPasswordValidator.IsCommon(password));

    [Theory]
    [InlineData("Quiet evening books 77")]
    [InlineData("correct horse battery staple")]
    public void Passphrases_are_allowed(string password) =>
        Assert.False(CommonPasswordValidator.IsCommon(password));
}

public class WatermarkTests
{
    [Fact]
    public void Watermark_keeps_page_size_and_changes_pixels()
    {
        using var surface = SKSurface.Create(new SKImageInfo(800, 1100));
        surface.Canvas.Clear(SKColors.White);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Webp, 90);
        var clean = data.ToArray();

        var marked = PageRenderer.ApplyWatermark(clean, new Watermark("reader@example.org", "0123456789abcdef"), DateTimeOffset.UtcNow);

        using var bitmap = SKBitmap.Decode(marked);
        Assert.Equal(800, bitmap.Width);
        Assert.Equal(1100, bitmap.Height);
        var nonWhite = 0;
        for (var y = 0; y < bitmap.Height; y += 4)
            for (var x = 0; x < bitmap.Width; x += 4)
                if (bitmap.GetPixel(x, y) != SKColors.White) nonWhite++;
        Assert.True(nonWhite > 100, "Expected the watermark to draw on the page.");
    }
}
