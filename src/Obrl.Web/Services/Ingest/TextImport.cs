using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Ganss.Xss;
using VersOne.Epub;

namespace Obrl.Web.Services.Ingest;

public sealed record TocEntry(string Title, int Section);

public sealed record TextResource(byte[] Data, string ContentType);

/// <summary>A reflowable book split into sections of cleaned HTML.</summary>
public sealed class TextBook
{
    public List<string> Sections { get; } = [];
    public List<TocEntry> Toc { get; } = [];
    public List<TextResource> Resources { get; } = [];
    public byte[]? CoverImage { get; set; }
    public string? Title { get; set; }
    public string? Author { get; set; }
    public string? Description { get; set; }
}

/// <summary>Turns EPUB and plain-text files into <see cref="TextBook"/>s.</summary>
public static partial class TextImport
{
    /// <summary>Largest section sent to the reader at once, in characters of HTML.</summary>
    public const int MaxSectionChars = 20_000;

    /// <summary>Placeholder attribute for images; the reader swaps it for a signed URL.</summary>
    public const string ResourceAttribute = "data-obrl-res";

    // ---------- Plain text ----------

    public static TextBook FromText(byte[] bytes)
    {
        var text = Decode(bytes).Replace("\r\n", "\n").Replace('\r', '\n');
        var book = new TextBook();
        var html = new StringBuilder();
        var blocks = text.Contains("\n\n")
            ? Regex.Split(text, @"\n\s*\n")
            : text.Split('\n');

        var pendingHeadings = new List<string>();
        var chunker = new Chunker(book.Sections);
        foreach (var raw in blocks)
        {
            var block = raw.Trim();
            if (block.Length == 0) continue;
            if (IsHeading(block))
            {
                // Start a new section at every chapter heading so the table of contents can jump to it.
                chunker.Flush();
                book.Toc.Add(new TocEntry(block, book.Sections.Count));
                chunker.Add($"<h2>{WebUtility.HtmlEncode(block)}</h2>");
            }
            else
            {
                var para = WebUtility.HtmlEncode(Regex.Replace(block, @"\s*\n\s*", " "));
                chunker.Add($"<p>{para}</p>");
            }
        }
        chunker.Flush();
        if (book.Sections.Count == 0) book.Sections.Add("<p></p>");
        if (book.Toc.Count == 0 || book.Toc[0].Section != 0) book.Toc.Insert(0, new TocEntry("Start", 0));
        return book;
    }

    private static bool IsHeading(string block) =>
        block.Length <= 80 && !block.Contains('\n') && HeadingRegex().IsMatch(block);

    [GeneratedRegex(@"^(chapter|part|book|prologue|epilogue|preface|introduction|contents)\b|^[IVXLC]+\.?$", RegexOptions.IgnoreCase)]
    private static partial Regex HeadingRegex();

    private static string Decode(byte[] bytes)
    {
        try
        {
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            return utf8.GetString(bytes).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    // ---------- EPUB ----------

    public static TextBook FromEpub(Stream epubStream)
    {
        EpubBook epub;
        try
        {
            epub = EpubReader.ReadBook(epubStream);
        }
        catch (Exception ex)
        {
            throw new IngestException("This EPUB could not be read: " + ex.Message);
        }

        var book = new TextBook
        {
            Title = NullIfBlank(epub.Title),
            Author = NullIfBlank(epub.Author),
            Description = NullIfBlank(epub.Description),
            CoverImage = epub.CoverImage,
        };

        // Images, addressed by their path inside the EPUB.
        var imageIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var image in epub.Content.Images.Local)
        {
            imageIndex[image.FilePath] = book.Resources.Count;
            book.Resources.Add(new TextResource(image.Content, image.ContentMimeType));
        }

        var sanitizer = CreateSanitizer();
        var parser = new HtmlParser();
        var firstSectionOfFile = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in epub.ReadingOrder)
        {
            var doc = parser.ParseDocument(file.Content ?? "");
            var body = doc.Body;
            if (body is null) continue;

            foreach (var img in body.QuerySelectorAll("img").ToList())
            {
                var src = img.GetAttribute("src");
                var resolved = src is null ? null : ResolvePath(file.FilePath, src);
                if (resolved is not null && imageIndex.TryGetValue(resolved, out var n))
                {
                    img.SetAttribute(ResourceAttribute, n.ToString());
                    img.RemoveAttribute("src");
                    img.RemoveAttribute("srcset");
                }
                else
                {
                    img.Remove();
                }
            }

            var clean = sanitizer.Sanitize(body.InnerHtml);
            if (string.IsNullOrWhiteSpace(StripTags(clean)) && !clean.Contains(ResourceAttribute)) continue;

            firstSectionOfFile[file.FilePath] = book.Sections.Count;
            var chunker = new Chunker(book.Sections);
            foreach (var piece in SplitHtml(clean, parser)) chunker.Add(piece);
            chunker.Flush();
        }

        if (book.Sections.Count == 0) throw new IngestException("This EPUB has no readable text.");

        foreach (var (title, filePath) in FlattenNavigation(epub.Navigation, depth: 0))
        {
            if (filePath is not null && firstSectionOfFile.TryGetValue(filePath, out var section))
                book.Toc.Add(new TocEntry(title, section));
        }
        if (book.Toc.Count == 0)
            book.Toc.AddRange(firstSectionOfFile.Values.Order().Select((s, i) => new TocEntry($"Part {i + 1}", s)));

        return book;
    }

    private static IEnumerable<(string Title, string? FilePath)> FlattenNavigation(List<EpubNavigationItem>? items, int depth)
    {
        if (items is null || depth > 2) yield break;
        foreach (var item in items)
        {
            var title = NullIfBlank(item.Title);
            if (title is not null) yield return (title, item.HtmlContentFile?.FilePath);
            foreach (var nested in FlattenNavigation(item.NestedItems, depth + 1)) yield return nested;
        }
    }

    /// <summary>Resolves a relative link inside an EPUB (e.g. "../Images/a.jpg" from "OEBPS/Text/c1.xhtml").</summary>
    public static string? ResolvePath(string fromFile, string href)
    {
        if (href.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || href.Contains("://")) return null;
        var baseUri = new Uri("epub:///" + fromFile.TrimStart('/'));
        if (!Uri.TryCreate(baseUri, href.Split('#')[0], out var resolved)) return null;
        return Uri.UnescapeDataString(resolved.AbsolutePath).TrimStart('/');
    }

    // ---------- Shared HTML helpers ----------

    public static HtmlSanitizer CreateSanitizer()
    {
        var s = new HtmlSanitizer();
        // Books get the reader's own typography and never link or load anything themselves.
        foreach (var attr in new[] { "style", "href", "src", "srcset", "action", "background", "width", "height" })
            s.AllowedAttributes.Remove(attr);
        foreach (var tag in new[] { "form", "input", "button", "textarea", "select", "option", "iframe", "video", "audio", "source", "font" })
            s.AllowedTags.Remove(tag);
        s.AllowDataAttributes = true;
        return s;
    }

    /// <summary>
    /// Splits HTML into top-level blocks no bigger than <see cref="MaxSectionChars"/> where possible.
    /// A single wrapper element that is too big is opened up and its children used instead.
    /// </summary>
    public static IEnumerable<string> SplitHtml(string html, HtmlParser? parser = null)
    {
        parser ??= new HtmlParser();
        var doc = parser.ParseDocument("<body>" + html + "</body>");
        return Flatten(doc.Body!.ChildNodes);
    }

    private static IEnumerable<string> Flatten(INodeList nodes)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case IElement el when el.OuterHtml.Length > MaxSectionChars && el.Children.Length > 0:
                    foreach (var inner in Flatten(el.ChildNodes)) yield return inner;
                    break;
                case IElement el:
                    yield return el.OuterHtml;
                    break;
                case IText text when !string.IsNullOrWhiteSpace(text.Data):
                    yield return "<p>" + WebUtility.HtmlEncode(text.Data.Trim()) + "</p>";
                    break;
            }
        }
    }

    private static string StripTags(string html) => Regex.Replace(html, "<[^>]+>", "").Trim();

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Packs HTML blocks into sections up to <see cref="MaxSectionChars"/>.</summary>
    private sealed class Chunker(List<string> sections)
    {
        private readonly StringBuilder _current = new();

        public void Add(string block)
        {
            if (_current.Length > 0 && _current.Length + block.Length > MaxSectionChars) Flush();
            _current.Append(block).Append('\n');
        }

        public void Flush()
        {
            if (_current.Length == 0) return;
            sections.Add(_current.ToString());
            _current.Clear();
        }
    }
}
