using Obrl.Web.Domain;

namespace Obrl.Web.Services.Ingest;

public static class BookFormats
{
    /// <summary>Formats shown as page images.</summary>
    public static readonly HashSet<string> PageFormats = ["pdf", "doc", "docx", "odt", "rtf"];

    /// <summary>Formats shown as reflowable text.</summary>
    public static readonly HashSet<string> TextFormats = ["epub", "txt", "md", "mobi", "azw", "azw3", "fb2"];

    /// <summary>Text formats that need Calibre to turn them into EPUB first.</summary>
    public static readonly HashSet<string> CalibreFormats = ["mobi", "azw", "azw3", "fb2"];

    public static string Accept => string.Join(',', PageFormats.Concat(TextFormats).Select(f => "." + f));

    public static string? Normalize(string fileName)
    {
        var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        return PageFormats.Contains(ext) || TextFormats.Contains(ext) ? ext : null;
    }

    public static ReaderMode ModeFor(string format) =>
        PageFormats.Contains(format) ? ReaderMode.Page : ReaderMode.Text;
}
