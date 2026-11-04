using System.Text.Json;
using Obrl.Web.Domain;
using Obrl.Web.Services.Rendering;
using PDFtoImage;

namespace Obrl.Web.Services.Ingest;

/// <summary>Turns an uploaded original into what the reader serves: a PDF for page mode, or HTML sections for text mode.</summary>
public sealed class BookProcessor(BookStorage storage, ExternalTools tools, CoverService covers, ILogger<BookProcessor> logger)
{
    public async Task ProcessAsync(Book book, CancellationToken ct)
    {
        var original = await storage.ReadDecryptedAsync(storage.OriginalPath(book.Id), ct);
        storage.ClearDerived(book.Id);
        var work = storage.CreateTempDir();
        try
        {
            if (book.Mode == ReaderMode.Page)
                await ProcessPageBookAsync(book, original, work, ct);
            else
                await ProcessTextBookAsync(book, original, work, ct);
        }
        finally
        {
            // The plain copy of the upload only ever lives in this folder, and only during conversion.
            try { Directory.Delete(work, recursive: true); }
            catch (Exception ex) { logger.LogWarning(ex, "Could not remove temp folder {Dir}", work); }
        }
    }

    private async Task ProcessPageBookAsync(Book book, byte[] original, string work, CancellationToken ct)
    {
        byte[] pdf;
        if (book.OriginalFormat == "pdf")
        {
            pdf = original;
        }
        else
        {
            var input = Path.Combine(work, "book." + book.OriginalFormat);
            await File.WriteAllBytesAsync(input, original, ct);
            pdf = await File.ReadAllBytesAsync(await tools.ToPdfWithLibreOfficeAsync(input, work, ct), ct);
        }

        int pages;
        try
        {
            pages = PdfiumLock.Run(() => Conversion.GetPageCount(pdf));
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "PDF for {Book} could not be opened", book.Id);
            throw new IngestException("The PDF could not be opened. It may be damaged or password protected.");
        }
        if (pages < 1) throw new IngestException("The document has no pages.");

        await storage.WriteEncryptedAsync(storage.ContentPdfPath(book.Id), pdf, ct);
        await covers.WriteForPageBookAsync(book, pdf, pages, ct);
        book.UnitCount = pages;
        book.TocJson = null;
    }

    private async Task ProcessTextBookAsync(Book book, byte[] original, string work, CancellationToken ct)
    {
        TextBook text;
        if (book.OriginalFormat is "txt" or "md")
        {
            text = TextImport.FromText(original);
        }
        else
        {
            var epubBytes = original;
            if (BookFormats.CalibreFormats.Contains(book.OriginalFormat))
            {
                var input = Path.Combine(work, "book." + book.OriginalFormat);
                await File.WriteAllBytesAsync(input, original, ct);
                epubBytes = await File.ReadAllBytesAsync(await tools.ToEpubWithCalibreAsync(input, work, ct), ct);
            }
            using var ms = new MemoryStream(epubBytes);
            text = TextImport.FromEpub(ms);
        }

        for (var i = 0; i < text.Sections.Count; i++)
            await storage.WriteEncryptedAsync(storage.SectionPath(book.Id, i), System.Text.Encoding.UTF8.GetBytes(text.Sections[i]), ct);
        for (var i = 0; i < text.Resources.Count; i++)
            await storage.WriteEncryptedAsync(storage.ResourcePath(book.Id, i), text.Resources[i].Data, ct);
        await storage.SaveResourceTypesAsync(book.Id, text.Resources.Select(r => r.ContentType).ToList(), ct);

        if (string.IsNullOrWhiteSpace(book.Author) && text.Author is not null) book.Author = Trim(text.Author, 300);
        if (string.IsNullOrWhiteSpace(book.Description) && text.Description is not null)
            book.Description = Trim(System.Text.RegularExpressions.Regex.Replace(text.Description, "<[^>]+>", ""), 4000);

        var cover = text.CoverImage is { Length: > 0 } ? CoverMaker.FromImage(text.CoverImage) : null;
        await covers.WriteForTextBookAsync(book, cover ?? CoverMaker.Generated(book.Title, book.Author), ct);
        book.UnitCount = text.Sections.Count;
        book.TocJson = JsonSerializer.Serialize(text.Toc);
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max];
}
