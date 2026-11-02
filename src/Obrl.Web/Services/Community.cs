using System.Text;
using Obrl.Web.Domain;

namespace Obrl.Web.Services;

/// <summary>Small rules shared by ratings, comments and the message board.</summary>
public static class Community
{
    public const int MaxCommentLength = 4000;
    public const string RemovedByAuthor = "Removed by the author";
    public const int MaxPostLength = 8000;
    public const int MaxTitleLength = 200;

    /// <summary>Ratings a book needs before it can appear on the "Top rated" shelf.</summary>
    public const int TopRatedMinimumVotes = 2;

    /// <summary>How many "average" votes every book starts with, so one 5-star vote does not beat fifty 4-star votes.</summary>
    public const double PriorWeight = 5;

    /// <summary>Bayesian average: pulls books with few ratings towards the library-wide mean.</summary>
    public static double WeightedScore(double average, int count, double libraryMean, double priorWeight = PriorWeight) =>
        count <= 0 ? libraryMean : (average * count + libraryMean * priorWeight) / (count + priorWeight);

    /// <summary>
    /// Tidies what people type: trims, normalises line endings, removes control characters,
    /// and squeezes runs of blank lines. Returns null when nothing is left.
    /// The text is always shown HTML-encoded, so no markup is ever interpreted.
    /// </summary>
    public static string? CleanText(string? input, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var sb = new StringBuilder(input.Length);
        var newlines = 0;
        foreach (var ch in input.Replace("\r\n", "\n").Replace('\r', '\n'))
        {
            if (ch == '\n')
            {
                if (++newlines <= 2) sb.Append('\n');
                continue;
            }
            if (char.IsControl(ch) && ch != '\t') continue;
            if (ch is '​' or '‎' or '‏' or '‪' or '‫' or '‬' or '‭' or '‮' or '⁦' or '⁧' or '⁨' or '⁩')
                continue; // zero-width and bidi overrides, used to disguise text
            newlines = 0;
            sb.Append(ch);
        }
        var text = sb.ToString().Trim();
        if (text.Length == 0) return null;
        return text.Length <= maxLength ? text : text[..maxLength].TrimEnd();
    }

    /// <summary>
    /// What a viewer sees of a comment thread. Hidden comments are left out for readers,
    /// except as a "removed" placeholder when visible replies hang under them.
    /// Moderators see everything, marked as hidden.
    /// </summary>
    public static List<CommentView> BuildThreads(IEnumerable<CommentRow> rows, string viewerId, bool isModerator)
    {
        var list = rows.ToList();
        var replies = list.Where(r => r.ParentId is not null).ToLookup(r => r.ParentId!.Value);
        var result = new List<CommentView>();
        foreach (var top in list.Where(r => r.ParentId is null).OrderByDescending(r => r.CreatedAt))
        {
            var visibleReplies = replies[top.Id]
                .Where(r => isModerator || !r.IsHidden)
                .OrderBy(r => r.CreatedAt)
                .Select(r => ToView(r, viewerId, isModerator, []))
                .ToList();
            if (top.IsHidden && !isModerator && visibleReplies.Count == 0) continue;
            result.Add(ToView(top, viewerId, isModerator, visibleReplies));
        }
        return result;
    }

    private static CommentView ToView(CommentRow r, string viewerId, bool isModerator, List<CommentView> replies)
    {
        var placeholder = r.IsHidden && !isModerator;
        return new CommentView(
            r.Id,
            placeholder ? null : r.AuthorName,
            placeholder ? null : r.Body,
            r.CreatedAt,
            r.IsHidden,
            placeholder ? null : r.HiddenReason,
            IsMine: r.UserId == viewerId,
            OpenReports: isModerator ? r.OpenReports : 0,
            ReportedByMe: r.ReportedByMe,
            replies);
    }
}

/// <summary>A comment as loaded from the database, before visibility rules.</summary>
public sealed record CommentRow(
    long Id,
    long? ParentId,
    string UserId,
    string AuthorName,
    string Body,
    DateTimeOffset CreatedAt,
    bool IsHidden,
    string? HiddenReason,
    int OpenReports,
    bool ReportedByMe);

/// <summary>A comment as the viewer sees it. Author and body are null for a removed placeholder.</summary>
public sealed record CommentView(
    long Id,
    string? AuthorName,
    string? Body,
    DateTimeOffset CreatedAt,
    bool IsHidden,
    string? HiddenReason,
    bool IsMine,
    int OpenReports,
    bool ReportedByMe,
    List<CommentView> Replies);
