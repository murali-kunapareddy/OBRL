using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Obrl.Web.Data;
using Obrl.Web.Domain;
using Obrl.Web.Services;

namespace Obrl.Tests;

/// <summary>The book limit for Users and Contributors, the subscription length, and Contributors' own books.</summary>
public class LendingTests : IDisposable
{
    private readonly ObrlDbContext _db;
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));
    private readonly Lending _lending;
    private readonly List<Book> _books = [];

    public LendingTests()
    {
        _db = new ObrlDbContext(new DbContextOptionsBuilder<ObrlDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        for (var i = 0; i < 8; i++)
            _books.Add(new Book { Title = $"Book {i}", Status = BookStatus.Ready, IsApproved = true, UploadedById = i == 7 ? "contributor" : "uploader" });
        _db.Books.AddRange(_books);
        _db.SaveChanges();
        _lending = new Lending(Options.Create(new ObrlOptions { MaxSubscribedBooks = 5 }), _clock);
    }

    private static ClaimsPrincipal Member(string id, string role) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id), new Claim(ClaimTypes.Role, role)], "test"));

    private Task<SubscribeResult> Subscribe(ClaimsPrincipal who, int book) => _lending.SubscribeAsync(_db, who, _books[book].Id, default);

    private List<string> Readable(string id, bool everything = false) =>
        AccessService.ReadableBooks(_db, id, everything, _clock.GetUtcNow()).Select(b => b.Title).OrderBy(t => t).ToList();

    [Fact]
    public async Task Users_can_hold_five_books_at_a_time()
    {
        var reader = Member("reader", Roles.User);
        for (var i = 0; i < 5; i++) Assert.Equal(SubscribeResult.Subscribed, await Subscribe(reader, i));
        Assert.Equal(SubscribeResult.LimitReached, await Subscribe(reader, 5));
        Assert.Equal(5, Readable("reader").Count);
    }

    [Fact]
    public async Task Unsubscribing_frees_a_slot()
    {
        var reader = Member("reader", Roles.User);
        for (var i = 0; i < 5; i++) await Subscribe(reader, i);
        _db.Subscriptions.Remove(_db.Subscriptions.First(s => s.BookId == _books[0].Id));
        _db.SaveChanges();
        Assert.Equal(SubscribeResult.Subscribed, await Subscribe(reader, 5));
    }

    [Fact]
    public async Task Subscriptions_end_after_the_loan_period_and_free_their_slot()
    {
        var reader = Member("reader", Roles.User);
        for (var i = 0; i < 5; i++) await Subscribe(reader, i);
        Assert.All(_db.Subscriptions, s => Assert.Equal(_clock.GetUtcNow().AddDays(Lending.DefaultLoanDays), s.ExpiresAt));

        _clock.Advance(TimeSpan.FromDays(Lending.DefaultLoanDays) + TimeSpan.FromSeconds(1));
        Assert.Empty(Readable("reader"));
        Assert.Equal(SubscribeResult.Subscribed, await Subscribe(reader, 5));
        // An ended subscription can be taken out again, which starts a new period.
        Assert.Equal(SubscribeResult.Subscribed, await Subscribe(reader, 0));
        Assert.Equal(["Book 0", "Book 5"], Readable("reader"));
    }

    [Fact]
    public async Task Subscribing_twice_does_not_use_another_slot()
    {
        var reader = Member("reader", Roles.User);
        await Subscribe(reader, 0);
        Assert.Equal(SubscribeResult.AlreadySubscribed, await Subscribe(reader, 0));
        Assert.Equal(1, await _lending.SlotsInUseAsync(_db, "reader", default));
    }

    [Fact]
    public async Task Contributors_have_the_same_limit_but_their_own_books_are_free_and_never_end()
    {
        var contributor = Member("contributor", Roles.Contributor);
        for (var i = 0; i < 5; i++) await Subscribe(contributor, i);
        Assert.Equal(SubscribeResult.LimitReached, await Subscribe(contributor, 5));

        _clock.Advance(TimeSpan.FromDays(365));
        Assert.Equal(["Book 7"], Readable("contributor"));
    }

    [Fact]
    public async Task Moderators_and_admins_have_no_limit_or_end_date()
    {
        var mod = Member("mod", Roles.Moderator);
        for (var i = 0; i < 7; i++) Assert.Equal(SubscribeResult.Subscribed, await Subscribe(mod, i));
        Assert.All(_db.Subscriptions.Where(s => s.UserId == "mod"), s => Assert.Null(s.ExpiresAt));
    }

    [Fact]
    public async Task No_end_date_when_the_admin_turns_the_loan_period_off()
    {
        await _lending.SaveAsync(_db, 0, "admin");
        await Subscribe(Member("reader", Roles.User), 0);
        Assert.Null(_db.Subscriptions.Single(s => s.UserId == "reader").ExpiresAt);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _lending.SaveAsync(_db, 3, "admin"));
    }

    public void Dispose() => _db.Dispose();
}
