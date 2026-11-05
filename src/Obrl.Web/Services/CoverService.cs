using Obrl.Web.Domain;
using Obrl.Web.Services.Rendering;

namespace Obrl.Web.Services;

/// <summary>
/// Decides and writes a book's cover. In order of preference: an image the uploader supplied, a page the uploader
/// picked (PDF and Word books; as they cropped and adjusted it, if they did), then the default: the first page, or for e-books their own cover or one made from the title.
/// </summary>
public sealed class CoverService(BookStorage storage)
{
    public const int MaxImageMegabytes = 10;
    public const string Accept = ".jpg,.jpeg,.png,.webp,.gif,.bmp,image/*";

    /// <summary>0-based page for the cover of a page-mode book. Falls back to the first page when the pick is out of range.</summary>
    public static int PageIndex(Book book, int pageCount) =>
        book.CoverSource == CoverSource.Page && book.CoverPage is int p && p >= 1 && p <= pageCount ? p - 1 : 0;

    /// <summary>Reads an uploaded image and turns it into a cover. Null when the file is not an image OBRL can read.</summary>
    public static async Task<byte[]?> PrepareCustomAsync(IFormFile file, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        try { return CoverMaker.FromImage(ms.ToArray()); }
        catch (Exception) { return null; }
    }

    public bool HasCustom(Guid id) => File.Exists(storage.CustomCoverPath(id));

    public Task SaveCustomAsync(Guid id, byte[] webp, CancellationToken ct) => WriteAsync(storage.CustomCoverPath(id), webp, ct);

    public bool HasPageCrop(Guid id) => File.Exists(storage.PageCoverPath(id));

    public Task SavePageCropAsync(Guid id, byte[] webp, CancellationToken ct) => WriteAsync(storage.PageCoverPath(id), webp, ct);

    public void DeletePageCrop(Guid id)
    {
        if (File.Exists(storage.PageCoverPath(id))) File.Delete(storage.PageCoverPath(id));
    }

    public void DeleteCustom(Guid id)
    {
        if (File.Exists(storage.CustomCoverPath(id))) File.Delete(storage.CustomCoverPath(id));
    }

    /// <summary>During conversion of a page-mode book.</summary>
    public async Task WriteForPageBookAsync(Book book, byte[] pdf, int pageCount, CancellationToken ct)
    {
        var chosen = await CustomFor(book, ct) ?? await PageCropFor(book, pageCount, ct);
        await WriteAsync(storage.CoverPath(book.Id), chosen ?? CoverMaker.FromPdf(pdf, PageIndex(book, pageCount)), ct);
        book.HasCover = true;
    }

    /// <summary>During conversion of a text-mode book. The default cover is kept so the uploader can switch back to it.</summary>
    public async Task WriteForTextBookAsync(Book book, byte[] defaultCover, CancellationToken ct)
    {
        await WriteAsync(storage.DefaultCoverPath(book.Id), defaultCover, ct);
        await WriteAsync(storage.CoverPath(book.Id), await CustomFor(book, ct) ?? defaultCover, ct);
        book.HasCover = true;
    }

    /// <summary>
    /// Text books converted before covers could be changed have no saved default cover. Call this before changing
    /// the cover choice, so the current (default) cover is kept for switching back later.
    /// </summary>
    public void KeepDefault(Book book)
    {
        if (book.Mode != ReaderMode.Text || book.CoverSource != CoverSource.Default) return;
        var current = storage.CoverPath(book.Id);
        var keep = storage.DefaultCoverPath(book.Id);
        if (File.Exists(current) && !File.Exists(keep))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(keep)!);
            File.Copy(current, keep);
        }
    }

    /// <summary>Rewrites the cover of a converted book after the uploader changed the choice. Books still converting get it from the converter.</summary>
    public async Task RefreshAsync(Book book, CancellationToken ct)
    {
        if (book.Status != BookStatus.Ready) return;
        if (book.Mode == ReaderMode.Page)
        {
            await WriteForPageBookAsync(book, await storage.GetContentPdfAsync(book.Id, ct), book.UnitCount, ct);
            return;
        }
        var cover = await CustomFor(book, ct);
        if (cover is null && File.Exists(storage.DefaultCoverPath(book.Id)))
            cover = await File.ReadAllBytesAsync(storage.DefaultCoverPath(book.Id), ct);
        await WriteAsync(storage.CoverPath(book.Id), cover ?? CoverMaker.Generated(book.Title, book.Author), ct);
        book.HasCover = true;
    }

    /// <summary>A small preview of how a page would look as the cover. Page-mode books only; <paramref name="page"/> is 1-based.</summary>
    public async Task<byte[]> PagePreviewAsync(Book book, int page, CancellationToken ct) =>
        CoverMaker.FromPdf(await storage.GetContentPdfAsync(book.Id, ct), page - 1);

    /// <summary>A whole page, for the crop tool on the edit form. Page-mode books only; <paramref name="page"/> is 1-based.</summary>
    public async Task<byte[]> WholePageAsync(Book book, int page, CancellationToken ct) =>
        CoverMaker.WholePage(await storage.GetContentPdfAsync(book.Id, ct), page - 1);

    /// <summary>The page as the uploader cropped it, while that page is still the chosen cover.</summary>
    private async Task<byte[]?> PageCropFor(Book book, int pageCount, CancellationToken ct) =>
        book.CoverSource == CoverSource.Page && book.CoverPage is int p && p >= 1 && p <= pageCount && HasPageCrop(book.Id)
            ? await File.ReadAllBytesAsync(storage.PageCoverPath(book.Id), ct) : null;

    private async Task<byte[]?> CustomFor(Book book, CancellationToken ct) =>
        book.CoverSource == CoverSource.Custom && HasCustom(book.Id) ? await File.ReadAllBytesAsync(storage.CustomCoverPath(book.Id), ct) : null;

    private static async Task WriteAsync(string path, byte[] bytes, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".part";
        await File.WriteAllBytesAsync(tmp, bytes, ct);
        File.Move(tmp, path, overwrite: true);
    }
}
