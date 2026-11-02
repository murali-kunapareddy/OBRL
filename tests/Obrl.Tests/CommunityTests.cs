using Obrl.Web.Services;

namespace Obrl.Tests;

public class CommunityTests
{
    [Fact]
    public void Clean_text_trims_and_squeezes_blank_lines()
    {
        Assert.Equal("Hello\n\nworld", Community.CleanText("  Hello\r\n\r\n\r\n\r\nworld  ", 100));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t ")]
    [InlineData("​‮")]
    public void Clean_text_returns_null_when_nothing_is_left(string? input) =>
        Assert.Null(Community.CleanText(input, 100));

    [Fact]
    public void Clean_text_removes_control_and_direction_override_characters() =>
        Assert.Equal("evil.exe", Community.CleanText("evil\u0007.‮exe", 100));

    [Fact]
    public void Clean_text_cuts_to_the_limit() =>
        Assert.Equal("abc", Community.CleanText("abcdef", 3));

    [Fact]
    public void One_perfect_vote_does_not_beat_many_good_ones()
    {
        var single = Community.WeightedScore(5.0, 1, libraryMean: 3.5);
        var many = Community.WeightedScore(4.5, 40, libraryMean: 3.5);
        Assert.True(many > single);
    }

    [Fact]
    public void Score_with_no_votes_is_the_library_mean() =>
        Assert.Equal(3.5, Community.WeightedScore(0, 0, 3.5));

    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-11-02T10:00:00Z");

    private static CommentRow Row(long id, long? parent, string user, int minutes, bool hidden = false, int reports = 0) =>
        new(id, parent, user, user.ToUpperInvariant(), $"body {id}", T0.AddMinutes(minutes), hidden, hidden ? "spam" : null, reports, false);

    [Fact]
    public void Threads_show_newest_first_with_replies_oldest_first()
    {
        var threads = Community.BuildThreads(
            [Row(1, null, "a", 0), Row(2, null, "b", 5), Row(3, 1, "c", 7), Row(4, 1, "d", 6)], "a", isModerator: false);
        Assert.Equal([2L, 1L], threads.Select(t => t.Id));
        Assert.Equal([4L, 3L], threads[1].Replies.Select(r => r.Id));
        Assert.True(threads[1].IsMine);
    }

    [Fact]
    public void Hidden_comments_are_left_out_for_readers()
    {
        var threads = Community.BuildThreads([Row(1, null, "a", 0, hidden: true), Row(2, null, "b", 1, reports: 2)], "x", false);
        var only = Assert.Single(threads);
        Assert.Equal(2, only.Id);
        Assert.Equal(0, only.OpenReports); // report counts are for Moderators only
    }

    [Fact]
    public void Hidden_comment_with_replies_becomes_a_placeholder_for_readers()
    {
        var threads = Community.BuildThreads([Row(1, null, "a", 0, hidden: true), Row(2, 1, "b", 1)], "x", false);
        var top = Assert.Single(threads);
        Assert.Null(top.Body);
        Assert.Null(top.AuthorName);
        Assert.Single(top.Replies);
    }

    [Fact]
    public void Moderators_see_hidden_comments_and_reports()
    {
        var threads = Community.BuildThreads([Row(1, null, "a", 0, hidden: true, reports: 3), Row(2, 1, "b", 1, hidden: true)], "mod", true);
        var top = Assert.Single(threads);
        Assert.Equal("body 1", top.Body);
        Assert.Equal(3, top.OpenReports);
        Assert.Single(top.Replies);
    }
}
