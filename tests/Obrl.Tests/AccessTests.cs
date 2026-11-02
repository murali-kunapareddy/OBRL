using Microsoft.EntityFrameworkCore;
using Obrl.Web.Data;
using Obrl.Web.Domain;
using Obrl.Web.Services;

namespace Obrl.Tests;

/// <summary>Who can read what: subscriptions to books, to categories, and to parents of categories.</summary>
public class AccessTests : IDisposable
{
    private readonly ObrlDbContext _db;
    private readonly Book _classic, _science, _uncategorised, _queued, _pending;

    public AccessTests()
    {
        _db = new ObrlDbContext(new DbContextOptionsBuilder<ObrlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        // Fiction (1) > Mystery (2) > Classic (3); Science (4)
        _db.Categories.AddRange(
            new Category { Id = 1, Name = "Fiction", Path = "/1/" },
            new Category { Id = 2, Name = "Mystery", ParentId = 1, Path = "/1/2/" },
            new Category { Id = 3, Name = "Classic", ParentId = 2, Path = "/1/2/3/" },
            new Category { Id = 4, Name = "Science", Path = "/4/" },
            new Category { Id = 14, Name = "Fiction-like path trap", Path = "/14/" });

        _classic = Book("Classic mystery", 3);
        _science = Book("Physics", 4);
        _uncategorised = Book("Trap", 14);
        _queued = Book("Not ready", 1);
        _queued.Status = BookStatus.Queued;
        _pending = Book("Awaiting review", 4);
        _pending.IsApproved = false;
        _pending.UploadedById = "contributor";
        _db.Books.AddRange(_classic, _science, _uncategorised, _queued, _pending);
        _db.SaveChanges();
    }

    private static Book Book(string title, int category) => new()
    {
        Title = title,
        Status = BookStatus.Ready,
        UploadedById = "uploader",
        Categories = [new BookCategory { CategoryId = category }],
    };

    private List<string> Readable(string userId, bool everything = false) =>
        AccessService.ReadableBooks(_db, userId, everything).Where(b => b.Status == BookStatus.Ready)
            .Select(b => b.Title).OrderBy(t => t).ToList();

    [Fact]
    public void No_subscriptions_means_nothing_to_read() => Assert.Empty(Readable("reader"));

    [Fact]
    public void Top_category_subscription_covers_every_level_below()
    {
        _db.Subscriptions.Add(new Subscription { UserId = "reader", CategoryId = 1 });
        _db.SaveChanges();
        Assert.Equal(["Classic mystery"], Readable("reader"));
    }

    [Fact]
    public void Sub_category_subscription_does_not_cover_siblings_or_lookalike_ids()
    {
        _db.Subscriptions.Add(new Subscription { UserId = "reader", CategoryId = 1 });
        _db.SaveChanges();
        // "/14/" must not match the "/1/" subscription.
        Assert.DoesNotContain("Trap", Readable("reader"));
    }

    [Fact]
    public void Single_book_subscription_covers_only_that_book()
    {
        _db.Subscriptions.Add(new Subscription { UserId = "reader", BookId = _science.Id });
        _db.SaveChanges();
        Assert.Equal(["Physics"], Readable("reader"));
    }

    [Fact]
    public void Subscriptions_are_per_user()
    {
        _db.Subscriptions.Add(new Subscription { UserId = "someone-else", CategoryId = 4 });
        _db.SaveChanges();
        Assert.Empty(Readable("reader"));
    }

    [Fact]
    public void Uploaders_can_read_their_own_books() =>
        Assert.Equal(["Classic mystery", "Physics", "Trap"], Readable("uploader"));

    [Fact]
    public void Moderators_and_admins_read_everything_that_is_ready() =>
        Assert.Equal(["Awaiting review", "Classic mystery", "Physics", "Trap"], Readable("mod", everything: true));

    [Fact]
    public void Unapproved_books_stay_locked_even_for_subscribers()
    {
        _db.Subscriptions.AddRange(
            new Subscription { UserId = "reader", CategoryId = 4 },
            new Subscription { UserId = "reader", BookId = _pending.Id });
        _db.SaveChanges();
        Assert.Equal(["Physics"], Readable("reader"));
    }

    [Fact]
    public void Contributors_read_their_own_books_while_waiting_for_review() =>
        Assert.Equal(["Awaiting review"], Readable("contributor"));

    [Fact]
    public void Unapproved_books_are_not_listed_in_the_library_for_others()
    {
        List<string> Visible(string user, bool everything = false) =>
            AccessService.VisibleBooks(_db, user, everything).Select(b => b.Title).ToList();
        Assert.DoesNotContain("Awaiting review", Visible("reader"));
        Assert.Contains("Awaiting review", Visible("contributor"));
        Assert.Contains("Awaiting review", Visible("mod", everything: true));
    }

    [Fact]
    public void Covered_check_matches_whole_path_segments()
    {
        Assert.True(AccessService.IsCovered("/1/2/3/", ["/1/"]));
        Assert.False(AccessService.IsCovered("/14/", ["/1/"]));
        Assert.False(AccessService.IsCovered("/1/", ["/1/2/"]));
    }

    public void Dispose() => _db.Dispose();
}
