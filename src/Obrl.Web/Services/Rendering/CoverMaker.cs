using PDFtoImage;
using SkiaSharp;

namespace Obrl.Web.Services.Rendering;

/// <summary>Makes 2:3 cover thumbnails (WebP) from a PDF page, an image, or the title.</summary>
public static class CoverMaker
{
    public const int Width = 360;
    public const int Height = 540;

    /// <param name="pageIndex">0-based page to use.</param>
    public static byte[] FromPdf(byte[] pdf, int pageIndex = 0)
    {
        using var bitmap = PdfiumLock.Run(() =>
            Conversion.ToImage(pdf, pageIndex, options: new RenderOptions(Width: Width, WithAspectRatio: true, BackgroundColor: SKColors.White)));
        return Fit(bitmap);
    }

    /// <summary>Crops and scales any image SkiaSharp can read (JPEG, PNG, WebP, GIF, BMP). Null when it isn't an image.</summary>
    public static byte[]? FromImage(byte[] imageBytes)
    {
        using var data = SKData.CreateCopy(imageBytes);
        using var codec = SKCodec.Create(data);
        if (codec is null) return null;
        using var bitmap = SKBitmap.Decode(codec);
        return bitmap is null || bitmap.Width < 1 || bitmap.Height < 1 ? null : Fit(bitmap);
    }

    /// <summary>A plain typographic cover: title and author on a colour picked from the title.</summary>
    public static byte[] Generated(string title, string? author)
    {
        var palette = new[] { "#2F4858", "#33658A", "#55828B", "#7A4E3B", "#5B3F6B", "#3F5E3A", "#8C5A2B", "#3D3B5C" };
        var colour = SKColor.Parse(palette[(int)((uint)StableHash(title) % palette.Length)]);
        using var surface = SKSurface.Create(new SKImageInfo(Width, Height));
        var c = surface.Canvas;
        c.Clear(colour);
        using var rule = new SKPaint { Color = SKColors.White.WithAlpha(90), StrokeWidth = 2, IsAntialias = true };
        c.DrawLine(32, 64, Width - 32, 64, rule);
        c.DrawLine(32, Height - 64, Width - 32, Height - 64, rule);

        using var titleFont = new SKFont(SkiaFonts.Sans, 30) { Embolden = true };
        using var authorFont = new SKFont(SkiaFonts.Sans, 18);
        using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var soft = new SKPaint { Color = SKColors.White.WithAlpha(200), IsAntialias = true };

        var y = 130f;
        foreach (var line in Wrap(title, titleFont, Width - 64).Take(6))
        {
            c.DrawText(line, 32, y, SKTextAlign.Left, titleFont, white);
            y += 38;
        }
        if (!string.IsNullOrWhiteSpace(author))
            c.DrawText(author, 32, Height - 90, SKTextAlign.Left, authorFont, soft);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Webp, 85);
        return data.ToArray();
    }

    private static byte[] Fit(SKBitmap source)
    {
        // Cover-crop to 2:3.
        var scale = Math.Max((float)Width / source.Width, (float)Height / source.Height);
        var w = source.Width * scale;
        var h = source.Height * scale;
        using var surface = SKSurface.Create(new SKImageInfo(Width, Height));
        surface.Canvas.Clear(SKColors.White);
        using var img = SKImage.FromBitmap(source);
        surface.Canvas.DrawImage(img, new SKRect((Width - w) / 2, 0, (Width + w) / 2, h),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        using var snap = surface.Snapshot();
        using var data = snap.Encode(SKEncodedImageFormat.Webp, 85);
        return data.ToArray();
    }

    private static IEnumerable<string> Wrap(string text, SKFont font, float maxWidth)
    {
        var line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (font.MeasureText(candidate) > maxWidth && line.Length > 0)
            {
                yield return line;
                line = word;
            }
            else line = candidate;
        }
        if (line.Length > 0) yield return line;
    }

    private static int StableHash(string s)
    {
        unchecked
        {
            var h = 17;
            foreach (var ch in s) h = h * 31 + ch;
            return h;
        }
    }
}

/// <summary>PDFium is not thread-safe; every call into it goes through this lock.</summary>
public static class PdfiumLock
{
    private static readonly Lock Gate = new();

    public static T Run<T>(Func<T> action)
    {
        lock (Gate) return action();
    }
}
