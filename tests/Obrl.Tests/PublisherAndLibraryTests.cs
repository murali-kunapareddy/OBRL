using Obrl.Web.Domain;
using Obrl.Web.Models;
using Obrl.Web.Services;

namespace Obrl.Tests;

public class PublisherTextTests
{
    private static KeyValuePair<string?, string?> Name(string? language, string? name) => KeyValuePair.Create(language, name);

    [Fact]
    public void Name_falls_back_to_English()
    {
        var p = new Publisher { Name = "Visalandhra", Translations = [new() { Language = "te", Name = "విశాలాంధ్ర" }] };
        Assert.Equal("విశాలాంధ్ర", PublisherText.Pick(p, "te"));
        Assert.Equal("Visalandhra", PublisherText.Pick(p, "kn"));
        Assert.Equal("Visalandhra", PublisherText.Pick(p, null));
    }

    [Fact]
    public void Saving_adds_updates_and_ignores_English_and_unknown_languages()
    {
        var p = new Publisher { Id = 7, Name = "Visalandhra", Translations = [new() { PublisherId = 7, Language = "te", Name = "old" }] };
        PublisherText.Apply(p, [Name("te", "  విశాలాంధ్ర   ప్రచురణ "), Name("kn", "ವಿಶಾಲಾಂಧ್ರ"), Name("en", "x"), Name("xx", "y")], emptyRemoves: true);
        Assert.Equal(2, p.Translations.Count);
        Assert.Equal("విశాలాంధ్ర ప్రచురణ", p.Translations.Single(t => t.Language == "te").Name);
        Assert.Equal("ವಿಶಾಲಾಂಧ್ರ", p.Translations.Single(t => t.Language == "kn").Name);
    }

    [Fact]
    public void An_empty_box_removes_a_name_only_when_the_form_showed_it()
    {
        Publisher Make() => new() { Name = "P", Translations = [new() { Language = "te", Name = "ప" }] };

        var shown = Make();
        PublisherText.Apply(shown, [Name("te", " ")], emptyRemoves: true);
        Assert.Empty(shown.Translations);

        // A book switched to this publisher: its boxes held another publisher's names, so emptiness means nothing.
        var notShown = Make();
        PublisherText.Apply(notShown, [Name("te", "")], emptyRemoves: false);
        Assert.Single(notShown.Translations);
    }
}

public class LibraryCountTests
{
    [Fact]
    public void Each_category_counts_its_own_books_and_those_below_it_once()
    {
        // Fiction (1) > Novels (2), Fiction > Stories (3); Poetry (4)
        var tree = new CategoryTree();
        var fiction = new CategoryNode(1, "Fiction", null, "/1/", 0, null);
        var novels = new CategoryNode(2, "Novels", 1, "/1/2/", 1, null);
        var stories = new CategoryNode(3, "Stories", 1, "/1/3/", 1, null);
        var poetry = new CategoryNode(4, "Poetry", null, "/4/", 0, null);
        foreach (var n in new[] { fiction, novels, stories, poetry }) tree.ById[n.Id] = n;

        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var counts = LibraryStats.CountPerCategory(tree, [(a, 2), (a, 3), (b, 3), (b, 4)]);

        Assert.Equal(2, counts[1]); // a and b, each once
        Assert.Equal(1, counts[2]);
        Assert.Equal(2, counts[3]);
        Assert.Equal(1, counts[4]);
    }
}
