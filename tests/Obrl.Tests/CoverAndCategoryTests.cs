using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Obrl.Web.Data;
using Obrl.Web.Domain;
using Obrl.Web.Models;
using Obrl.Web.Services;
using Obrl.Web.Services.Rendering;
using SkiaSharp;

namespace Obrl.Tests;

public class CategoryTextTests
{
    [Fact]
    public void Name_and_description_fall_back_to_English_when_left_empty()
    {
        Assert.Equal(("కథలు", "Short stories"), CategoryText.Pick("Stories", "Short stories", new CategoryTranslation { Language = "te", Name = "కథలు", Description = " " }));
        Assert.Equal(("Stories", (string?)null), CategoryText.Pick("Stories", null, null));
    }

    [Fact]
    public void Saving_the_form_adds_updates_and_removes_translations()
    {
        var category = new Category { Name = "Stories" };
        CategoryText.Apply(category,
        [
            new() { Language = "te", Name = " కథలు " },
            new() { Language = "kn", Name = "ಕಥೆಗಳು", Description = "ಸಣ್ಣ ಕಥೆಗಳು" },
            new() { Language = "en", Name = "ignored: English lives on the category" },
            new() { Language = "xx", Name = "ignored: not offered" },
        ]);
        Assert.Equal(["kn", "te"], category.Translations.Select(t => t.Language).Order());
        Assert.Equal("కథలు", category.Translations.Single(t => t.Language == "te").Name);

        CategoryText.Apply(category, [new() { Language = "te", Name = "" }, new() { Language = "kn", Name = "ಕಥೆ" }]);
        var kn = Assert.Single(category.Translations);
        Assert.Equal("ಕಥೆ", kn.Name);
        Assert.Null(kn.Description);
    }

    [Fact]
    public void Category_form_has_one_tab_per_other_language_with_saved_text()
    {
        var vm = new CategoryEditVm();
        vm.FillTranslations([new CategoryTranslation { Language = "te", Name = "కథలు" }]);
        Assert.Equal(["te", "kn"], vm.Translations.Select(t => t.Language));
        Assert.Equal("కథలు", vm.Translations[0].Name);
        Assert.True(vm.Translations[1].IsEmpty);
    }

    [Fact]
    public async Task Category_tree_shows_names_in_the_members_language_with_English_fallback()
    {
        using var db = new ObrlDbContext(new DbContextOptionsBuilder<ObrlDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Categories.AddRange(
            new Category { Id = 1, Name = "Stories", Path = "/1/", Translations = [new() { Language = "te", Name = "కథలు" }] },
            new Category { Id = 2, Name = "Poetry", Path = "/2/" },
            new Category { Id = 3, Name = "Folk tales", ParentId = 1, Path = "/1/3/", Translations = [new() { Language = "kn", Name = "ಜಾನಪದ ಕಥೆಗಳು" }] });
        await db.SaveChangesAsync();

        var saved = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("te");
            var tree = await CategoryTree.LoadAsync(db);
            Assert.Equal("కథలు", tree.ById[1].Name);
            Assert.Equal("Poetry", tree.ById[2].Name);
            Assert.Equal("Folk tales", tree.ById[3].Name); // only Kannada was entered
            Assert.Equal(["కథలు", "Folk tales"], tree.Trail(3).Select(n => n.Name));

            var english = await CategoryTree.LoadAsync(db, englishOnly: true);
            Assert.Equal("Stories", english.ById[1].Name);
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = saved;
        }
    }
}

public sealed class CoverTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "obrl-cover-" + Guid.NewGuid().ToString("N"));
    private readonly BookStorage storage;
    private readonly CoverService covers;

    public CoverTests()
    {
        storage = new BookStorage(Options.Create(new ObrlOptions { StorageRoot = root }), new FileEncryptor(new byte[32]));
        covers = new CoverService(storage);
    }

    public void Dispose()
    {
        storage.Dispose();
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Theory]
    [InlineData(CoverSource.Default, 5, 0)]
    [InlineData(CoverSource.Page, 5, 4)]
    [InlineData(CoverSource.Page, 11, 0)] // beyond the last page: first page
    [InlineData(CoverSource.Page, 0, 0)]
    [InlineData(CoverSource.Custom, 5, 0)]
    public void Picked_page_is_used_only_when_it_exists(CoverSource source, int page, int expectedIndex)
    {
        var book = new Book { CoverSource = source, CoverPage = page };
        Assert.Equal(expectedIndex, CoverService.PageIndex(book, pageCount: 10));
    }

    [Fact]
    public void Uploaded_images_are_cropped_to_a_book_cover_and_junk_is_refused()
    {
        var webp = CoverMaker.FromImage(Png(800, 600, SKColors.Red));
        Assert.NotNull(webp);
        using var decoded = SKBitmap.Decode(webp);
        Assert.Equal((CoverMaker.Width, CoverMaker.Height), (decoded.Width, decoded.Height));
        Assert.Null(CoverMaker.FromImage("not an image"u8.ToArray()));
    }

    [Fact]
    public async Task Text_book_switches_between_its_own_cover_and_a_custom_image()
    {
        var book = new Book { Title = "The Guide", Mode = ReaderMode.Text, Status = BookStatus.Ready };
        var own = CoverMaker.FromImage(Png(360, 540, SKColors.Blue))!;
        await covers.WriteForTextBookAsync(book, own, default);
        Assert.Equal(own, await File.ReadAllBytesAsync(storage.CoverPath(book.Id)));

        var custom = CoverMaker.FromImage(Png(360, 540, SKColors.Green))!;
        await covers.SaveCustomAsync(book.Id, custom, default);
        book.CoverSource = CoverSource.Custom;
        await covers.RefreshAsync(book, default);
        Assert.Equal(custom, await File.ReadAllBytesAsync(storage.CoverPath(book.Id)));

        book.CoverSource = CoverSource.Default;
        await covers.RefreshAsync(book, default);
        Assert.Equal(own, await File.ReadAllBytesAsync(storage.CoverPath(book.Id)));
        Assert.True(covers.HasCustom(book.Id)); // kept, so the uploader can switch back
    }

    [Fact]
    public async Task Older_text_books_keep_their_cover_when_the_uploader_changes_it()
    {
        // Converted before covers could be chosen: only covers/{id}.webp exists.
        var book = new Book { Title = "Old", Mode = ReaderMode.Text, Status = BookStatus.Ready };
        var own = CoverMaker.FromImage(Png(360, 540, SKColors.Blue))!;
        Directory.CreateDirectory(Path.GetDirectoryName(storage.CoverPath(book.Id))!);
        await File.WriteAllBytesAsync(storage.CoverPath(book.Id), own);

        covers.KeepDefault(book);
        await covers.SaveCustomAsync(book.Id, CoverMaker.FromImage(Png(360, 540, SKColors.Green))!, default);
        book.CoverSource = CoverSource.Custom;
        await covers.RefreshAsync(book, default);
        book.CoverSource = CoverSource.Default;
        await covers.RefreshAsync(book, default);
        Assert.Equal(own, await File.ReadAllBytesAsync(storage.CoverPath(book.Id)));
    }

    private static byte[] Png(int w, int h, SKColor colour)
    {
        using var bitmap = new SKBitmap(w, h);
        bitmap.Erase(colour);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
