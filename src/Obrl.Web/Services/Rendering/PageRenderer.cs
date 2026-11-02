using Microsoft.Extensions.Options;
using PDFtoImage;
using SkiaSharp;

namespace Obrl.Web.Services.Rendering;

/// <summary>
/// Renders one page of a page-mode book to a WebP image with the reader's watermark burned in.
/// Clean renders are cached (encrypted) per page and width; the watermark is added per request.
/// </summary>
public sealed class PageRenderer(BookStorage storage, IOptions<ObrlOptions> options, TimeProvider clock)
{
    public int SnapWidth(int requested)
    {
        var widths = options.Value.PageWidths.Order().ToArray();
        return widths.FirstOrDefault(w => w >= requested, widths[^1]);
    }

    public async Task<byte[]> RenderAsync(Guid bookId, int page, int width, Watermark watermark, CancellationToken ct)
    {
        width = SnapWidth(width);
        var clean = await GetCleanPageAsync(bookId, page, width, ct);
        return ApplyWatermark(clean, watermark, clock.GetUtcNow());
    }

    private async Task<byte[]> GetCleanPageAsync(Guid bookId, int page, int width, CancellationToken ct)
    {
        var cachePath = storage.CachePath(bookId, page, width);
        var cached = await storage.TryReadDecryptedAsync(cachePath, ct);
        if (cached is not null) return cached;

        var pdf = await storage.GetContentPdfAsync(bookId, ct);
        using var bitmap = PdfiumLock.Run(() =>
            Conversion.ToImage(pdf, page, options: new RenderOptions(Width: width, WithAspectRatio: true, BackgroundColor: SKColors.White)));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Webp, 92);
        var bytes = data.ToArray();
        await storage.WriteEncryptedAsync(cachePath, bytes, ct);
        return bytes;
    }

    /// <summary>
    /// Adds a faint diagonal pattern with the reader's name and id across the page,
    /// plus a small visible line at the bottom. Any leaked image names the account it came from.
    /// </summary>
    public static byte[] ApplyWatermark(byte[] cleanWebp, Watermark wm, DateTimeOffset now)
    {
        using var bitmap = SKBitmap.Decode(cleanWebp) ?? throw new InvalidOperationException("Cached page is unreadable.");
        using var surface = SKSurface.Create(new SKImageInfo(bitmap.Width, bitmap.Height));
        var c = surface.Canvas;
        using (var source = SKImage.FromBitmap(bitmap)) c.DrawImage(source, 0, 0, SKSamplingOptions.Default);

        var scale = bitmap.Width / 1000f;
        var stamp = $"{wm.Name} · {wm.UserId[..Math.Min(8, wm.UserId.Length)]} · {now:yyyy-MM-dd HH:mm} UTC";

        using var tileFont = new SKFont(SkiaFonts.Sans, 18 * scale);
        using var tilePaint = new SKPaint { Color = new SKColor(0, 0, 0, 22), IsAntialias = true };
        c.Save();
        c.RotateDegrees(-30, bitmap.Width / 2f, bitmap.Height / 2f);
        var stepX = tileFont.MeasureText(stamp) + 120 * scale;
        var stepY = 150 * scale;
        var diagonal = (float)Math.Sqrt(bitmap.Width * bitmap.Width + bitmap.Height * bitmap.Height);
        var row = 0;
        for (var y = -diagonal; y < diagonal * 1.5f; y += stepY, row++)
        {
            var offset = row % 2 == 0 ? 0 : stepX / 2;
            for (var x = -diagonal + offset; x < diagonal * 1.5f; x += stepX)
                c.DrawText(stamp, x, y, SKTextAlign.Left, tileFont, tilePaint);
        }
        c.Restore();

        using var footFont = new SKFont(SkiaFonts.Sans, 13 * scale);
        using var footPaint = new SKPaint { Color = new SKColor(0, 0, 0, 110), IsAntialias = true };
        c.DrawText($"Licensed to {stamp} · OBRL", bitmap.Width - 16 * scale, bitmap.Height - 12 * scale,
            SKTextAlign.Right, footFont, footPaint);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Webp, 82);
        return data.ToArray();
    }
}

public sealed record Watermark(string Name, string UserId);
