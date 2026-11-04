using Obrl.Web.Domain;
using Obrl.Web.Models;
using Obrl.Web.Services;

namespace Obrl.Tests;

public class BookTextTests
{
    private static Book English() => new() { Title = "The Guide", Author = "R. K. Narayan", Description = "A tour guide becomes a holy man." };

    [Fact]
    public void Each_field_falls_back_to_English_when_the_translation_leaves_it_empty()
    {
        var book = English();
        var text = LocalizedBook.From(book, new BookTranslation { Language = "te", Title = "గైడ్", Author = "  " });
        Assert.Equal("గైడ్", text.Title);
        Assert.Equal("R. K. Narayan", text.Author);
        Assert.Equal("A tour guide becomes a holy man.", text.Description);
        Assert.Equal(new LocalizedBook(book.Title, book.Author, book.Description), LocalizedBook.From(book, null));
    }

    [Fact]
    public void Saving_the_form_adds_updates_and_removes_translations()
    {
        var book = English();
        BookText.Apply(book,
        [
            new() { Language = "te", Title = " గైడ్ " },
            new() { Language = "kn", Title = "ಗೈಡ್", Description = "ವಿವರಣೆ" },
            new() { Language = "en", Title = "ignored: English lives on the book" },
            new() { Language = "xx", Title = "ignored: not offered" },
        ]);
        Assert.Equal(["kn", "te"], book.Translations.Select(t => t.Language).Order());
        Assert.Equal("గైడ్", book.Translations.Single(t => t.Language == "te").Title);

        // Clearing every field of a language removes it; other languages stay.
        BookText.Apply(book, [new() { Language = "te", Title = "" }, new() { Language = "kn", Title = "ಗೈಡ್ 2" }]);
        var kn = Assert.Single(book.Translations);
        Assert.Equal("ಗೈಡ್ 2", kn.Title);
        Assert.Null(kn.Description);
    }

    [Fact]
    public void Shelf_cards_take_the_translated_title_and_author()
    {
        var id = Guid.NewGuid();
        var other = Guid.NewGuid();
        var cards = new List<BookCard>
        {
            new(id, "The Guide", "R. K. Narayan", false, ReaderMode.Text, true),
            new(other, "Malgudi Days", null, false, ReaderMode.Text, true),
        };
        var map = new Dictionary<Guid, BookTranslation> { [id] = new() { BookId = id, Language = "kn", Title = "ಗೈಡ್" } };
        var result = BookText.Localize(cards, map);
        Assert.Equal("ಗೈಡ್", result[0].Title);
        Assert.Equal("R. K. Narayan", result[0].Author);
        Assert.Equal("Malgudi Days", result[1].Title);
    }

    [Fact]
    public void Upload_form_has_one_tab_per_other_language_with_saved_text()
    {
        var vm = new UploadVm();
        vm.FillTranslations([new BookTranslation { Language = "kn", Title = "ಗೈಡ್" }]);
        Assert.Equal(["te", "kn"], vm.Translations.Select(t => t.Language));
        Assert.True(vm.Translations[0].IsEmpty);
        Assert.Equal("ಗೈಡ್", vm.Translations[1].Title);
    }
}

public class BuildInfoTests
{
    [Fact]
    public void Build_number_is_the_build_time_in_hex()
    {
        Assert.Equal("126D67E87988", BuildInfo.ToBuildNumber(new DateTime(2026, 11, 4, 2, 39, 44, DateTimeKind.Utc)));
        Assert.Equal(20261104023944, Convert.ToInt64("126D67E87988", 16));
    }

    [Fact]
    public void Version_is_major_minor_and_the_build_number_of_this_build()
    {
        Assert.NotNull(BuildInfo.BuiltAt);
        Assert.Matches(@"^\d+\.\d+\.[0-9A-F]+$", BuildInfo.Version);
        Assert.Equal($"{BuildInfo.MajorMinor}.{BuildInfo.ToBuildNumber(BuildInfo.BuiltAt!.Value)}", BuildInfo.Version);
    }
}
