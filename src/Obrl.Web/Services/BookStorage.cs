using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Obrl.Web.Services;

/// <summary>
/// Disk layout under <see cref="ObrlOptions.StorageRoot"/>:
/// <code>
/// books/{id}/original.enc        uploaded file, encrypted
/// books/{id}/content.pdf.enc     page mode: the PDF that pages are rendered from
/// books/{id}/sections/00000.enc  text mode: cleaned HTML, one file per section
/// books/{id}/res/0.enc           text mode: images used by the sections
/// books/{id}/res/types.json      text mode: content type of each image
/// books/{id}/custom-cover.webp    cover image the uploader supplied (already cropped to 2:3)
/// books/{id}/default-cover.webp   text mode: the e-book's own cover, or one made from the title
/// cache/{id}/{page}_{width}.enc  rendered pages (without watermark), safe to delete
/// covers/{id}.webp               cover thumbnails, shown to signed-in users
/// tmp/                           scratch space for conversions
/// </code>
/// Everything except covers is encrypted with <see cref="FileEncryptor"/>.
/// </summary>
public sealed class BookStorage(IOptions<ObrlOptions> options, FileEncryptor encryptor) : IDisposable
{
    /// <summary>Own cache with a byte budget for decrypted PDFs, separate from the app-wide cache.</summary>
    private readonly MemoryCache memoryCache = new(new MemoryCacheOptions { SizeLimit = 512L * 1024 * 1024 });

    private readonly string _root = Path.GetFullPath(options.Value.StorageRoot);

    public string Root => _root;
    public string BookDir(Guid id) => Path.Combine(_root, "books", id.ToString("N"));
    public string OriginalPath(Guid id) => Path.Combine(BookDir(id), "original.enc");
    public string ContentPdfPath(Guid id) => Path.Combine(BookDir(id), "content.pdf.enc");
    public string SectionPath(Guid id, int n) => Path.Combine(BookDir(id), "sections", $"{n:D5}.enc");
    public string ResourcePath(Guid id, int n) => Path.Combine(BookDir(id), "res", $"{n}.enc");
    public string ResourceTypesPath(Guid id) => Path.Combine(BookDir(id), "res", "types.json");
    public string CachePath(Guid id, int page, int width) => Path.Combine(_root, "cache", id.ToString("N"), $"{page}_{width}.enc");
    public string CoverPath(Guid id) => Path.Combine(_root, "covers", $"{id:N}.webp");
    public string CustomCoverPath(Guid id) => Path.Combine(BookDir(id), "custom-cover.webp");
    public string DefaultCoverPath(Guid id) => Path.Combine(BookDir(id), "default-cover.webp");

    public string CreateTempDir()
    {
        var dir = Path.Combine(_root, "tmp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public async Task WriteEncryptedAsync(string path, byte[] plain, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".part";
        await File.WriteAllBytesAsync(tmp, encryptor.Encrypt(plain), ct);
        File.Move(tmp, path, overwrite: true);
    }

    public async Task<byte[]> ReadDecryptedAsync(string path, CancellationToken ct = default) =>
        encryptor.Decrypt(await File.ReadAllBytesAsync(path, ct));

    public async Task<byte[]?> TryReadDecryptedAsync(string path, CancellationToken ct = default) =>
        File.Exists(path) ? await ReadDecryptedAsync(path, ct) : null;

    /// <summary>
    /// The decrypted PDF of a page-mode book, kept in memory for a few minutes so that reading
    /// page after page does not decrypt the whole file each time.
    /// </summary>
    public async Task<byte[]> GetContentPdfAsync(Guid id, CancellationToken ct = default)
    {
        var key = "pdf:" + id;
        if (memoryCache.TryGetValue(key, out byte[]? cached) && cached is not null) return cached;
        var bytes = await ReadDecryptedAsync(ContentPdfPath(id), ct);
        memoryCache.Set(key, bytes, new MemoryCacheEntryOptions
        {
            Size = bytes.Length,
            SlidingExpiration = TimeSpan.FromMinutes(5),
        });
        return bytes;
    }

    public async Task SaveResourceTypesAsync(Guid id, IReadOnlyList<string> types, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ResourceTypesPath(id))!);
        await File.WriteAllTextAsync(ResourceTypesPath(id), JsonSerializer.Serialize(types), ct);
    }

    public async Task<IReadOnlyList<string>> GetResourceTypesAsync(Guid id, CancellationToken ct = default)
    {
        var key = "res:" + id;
        if (memoryCache.TryGetValue(key, out IReadOnlyList<string>? cached) && cached is not null) return cached;
        var path = ResourceTypesPath(id);
        IReadOnlyList<string> types = File.Exists(path)
            ? JsonSerializer.Deserialize<List<string>>(await File.ReadAllTextAsync(path, ct)) ?? []
            : [];
        memoryCache.Set(key, types, new MemoryCacheEntryOptions { Size = 1, SlidingExpiration = TimeSpan.FromMinutes(10) });
        return types;
    }

    /// <summary>Removes derived content and cache (keeps the original) so a book can be converted again.</summary>
    public void ClearDerived(Guid id)
    {
        foreach (var sub in new[] { "sections", "res" })
        {
            var dir = Path.Combine(BookDir(id), sub);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        if (File.Exists(ContentPdfPath(id))) File.Delete(ContentPdfPath(id));
        ClearCache(id);
    }

    public void ClearCache(Guid id)
    {
        memoryCache.Remove("pdf:" + id);
        memoryCache.Remove("res:" + id);
        var cacheDir = Path.Combine(_root, "cache", id.ToString("N"));
        if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, recursive: true);
    }

    public void DeleteAll(Guid id)
    {
        ClearCache(id);
        if (Directory.Exists(BookDir(id))) Directory.Delete(BookDir(id), recursive: true);
        if (File.Exists(CoverPath(id))) File.Delete(CoverPath(id));
    }

    public void Dispose() => memoryCache.Dispose();
}
