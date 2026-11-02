using SkiaSharp;

namespace Obrl.Web.Services.Rendering;

/// <summary>
/// The Linux SkiaSharp build OBRL uses has no fontconfig, so text needs a font loaded from a file.
/// DejaVu Sans ships with Ubuntu (package fonts-dejavu-core) and with LibreOffice.
/// </summary>
public static class SkiaFonts
{
    private static readonly string[] Candidates =
    [
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
        "/usr/share/fonts/truetype/noto/NotoSans-Regular.ttf",
        "/usr/share/fonts/TTF/DejaVuSans.ttf",
        "/Library/Fonts/Arial.ttf",
        "C:\\Windows\\Fonts\\arial.ttf",
    ];

    private static readonly Lazy<SKTypeface> Lazy = new(() =>
    {
        foreach (var path in Candidates)
        {
            if (File.Exists(path))
            {
                var tf = SKTypeface.FromFile(path);
                if (tf is not null) return tf;
            }
        }
        return SKTypeface.Default;
    });

    public static SKTypeface Sans => Lazy.Value;
}
