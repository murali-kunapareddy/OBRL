using System.IO.Compression;
using System.Text;
using Obrl.Web.Services.Ingest;

namespace Obrl.Tests;

public class TextImportTests
{
    [Fact]
    public void Plain_text_becomes_paragraphs_with_chapter_headings_in_the_contents()
    {
        var text = "Chapter 1\n\nFirst paragraph\ncontinues here.\n\nSecond <b>paragraph</b>.\n\nChapter 2\n\nThird.";
        var book = TextImport.FromText(Encoding.UTF8.GetBytes(text));

        var html = string.Join("", book.Sections);
        Assert.Contains("<h2>Chapter 1</h2>", html);
        Assert.Contains("<p>First paragraph continues here.</p>", html);
        Assert.Contains("&lt;b&gt;paragraph&lt;/b&gt;", html); // text is escaped, never rendered as HTML
        Assert.Equal(["Chapter 1", "Chapter 2"], book.Toc.Select(t => t.Title));
        Assert.Equal(2, book.Sections.Count); // each chapter starts a section
    }

    [Fact]
    public void Long_text_is_split_into_sections_under_the_size_limit()
    {
        var text = string.Join("\n\n", Enumerable.Range(0, 2000).Select(i => $"Paragraph {i} " + new string('x', 200)));
        var book = TextImport.FromText(Encoding.UTF8.GetBytes(text));
        Assert.True(book.Sections.Count > 10);
        Assert.All(book.Sections, s => Assert.True(s.Length <= TextImport.MaxSectionChars + 500));
    }

    [Fact]
    public void Non_utf8_text_is_read_as_latin1()
    {
        var bytes = Encoding.Latin1.GetBytes("Café crème");
        var book = TextImport.FromText(bytes);
        Assert.Contains("Café crème", System.Net.WebUtility.HtmlDecode(book.Sections[0]));
    }

    [Fact]
    public void Epub_is_cleaned_and_images_are_mapped()
    {
        using var epub = BuildEpub();
        var book = TextImport.FromEpub(epub);

        Assert.Equal("Test Book", book.Title);
        Assert.Equal("Ann Author", book.Author);
        var html = string.Join("", book.Sections);
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("href=", html);
        Assert.DoesNotContain("style=", html);
        Assert.DoesNotContain("onclick", html);
        Assert.Contains("data-obrl-res=\"0\"", html);
        Assert.Single(book.Resources);
        Assert.Equal("image/png", book.Resources[0].ContentType);
        Assert.Equal(["Opening", "Ending"], book.Toc.Select(t => t.Title));
        Assert.Equal(0, book.Toc[0].Section);
    }

    [Theory]
    [InlineData("OEBPS/Text/c1.xhtml", "../Images/a.png", "OEBPS/Images/a.png")]
    [InlineData("OEBPS/c1.xhtml", "img/a%20b.png", "OEBPS/img/a b.png")]
    [InlineData("c1.xhtml", "a.png#x", "a.png")]
    public void Relative_epub_links_resolve(string from, string href, string expected) =>
        Assert.Equal(expected, TextImport.ResolvePath(from, href));

    [Fact]
    public void External_links_do_not_resolve() =>
        Assert.Null(TextImport.ResolvePath("OEBPS/c1.xhtml", "https://example.org/a.png"));

    private static MemoryStream BuildEpub()
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, string content)
            {
                using var w = new StreamWriter(zip.CreateEntry(name, name == "mimetype" ? CompressionLevel.NoCompression : CompressionLevel.Optimal).Open());
                w.Write(content);
            }
            Add("mimetype", "application/epub+zip");
            Add("META-INF/container.xml", """<?xml version="1.0"?><container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container"><rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles></container>""");
            Add("OEBPS/content.opf", """
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="id">
                  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:identifier id="id">urn:uuid:1</dc:identifier><dc:title>Test Book</dc:title><dc:creator>Ann Author</dc:creator><dc:language>en</dc:language></metadata>
                  <manifest>
                    <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>
                    <item id="c1" href="Text/c1.xhtml" media-type="application/xhtml+xml"/>
                    <item id="c2" href="Text/c2.xhtml" media-type="application/xhtml+xml"/>
                    <item id="img" href="Images/a.png" media-type="image/png"/>
                  </manifest>
                  <spine><itemref idref="c1"/><itemref idref="c2"/></spine>
                </package>
                """);
            Add("OEBPS/nav.xhtml", """<?xml version="1.0" encoding="utf-8"?><html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops"><head><title>n</title></head><body><nav epub:type="toc"><ol><li><a href="Text/c1.xhtml">Opening</a></li><li><a href="Text/c2.xhtml">Ending</a></li></ol></nav></body></html>""");
            Add("OEBPS/Text/c1.xhtml", """<?xml version="1.0" encoding="utf-8"?><html xmlns="http://www.w3.org/1999/xhtml"><head><title>1</title></head><body style="color:red"><h1>One</h1><p onclick="x()">Hello <a href="https://evil.example">there</a></p><img src="../Images/a.png" alt="pic"/><script>alert(1)</script></body></html>""");
            Add("OEBPS/Text/c2.xhtml", """<?xml version="1.0" encoding="utf-8"?><html xmlns="http://www.w3.org/1999/xhtml"><head><title>2</title></head><body><h1>Two</h1><p>Bye.</p></body></html>""");
            using (var img = zip.CreateEntry("OEBPS/Images/a.png").Open())
                img.Write(Convert.FromHexString("89504E470D0A1A0A0000000D49484452000000010000000108060000001F15C4890000000D4944415478DA63F8FFFF3F0005FE02FEA7358AA50000000049454E44AE426082"));
        }
        ms.Position = 0;
        return ms;
    }
}
