namespace PrStatusTracker.Models;

public enum ReviewDecision { ReviewRequired, ChangesRequested, Approved }

public enum Mergeable { Unknown, Mergeable, Conflicting }

public enum CheckState { Success, Pending, Failure }

public enum ReviewState { Approved, ChangesRequested, Commented, Dismissed, Pending }

public sealed record Review(string Author, ReviewState State, DateTimeOffset SubmittedAt);

/// <summary>Raw pull request data as returned by GitHub, before any interpretation.</summary>
public sealed record PullRequest(
    string Repo,
    int Number,
    string Title,
    string Url,
    bool IsDraft,
    string Author,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int Additions,
    int Deletions,
    IReadOnlyList<string> Labels,
    ReviewDecision? ReviewDecision,
    Mergeable Mergeable,
    IReadOnlyList<string> RequestedReviewers,
    IReadOnlyList<Review> LatestReviews,
    DateTimeOffset? HeadCommittedAt,
    CheckState? Checks,
    bool HasOpenThreads = false,
    string BaseBranch = "",
    string HeadBranch = "",
    string DefaultBranch = "",
    IReadOnlyList<Review>? StandingReviews = null,
    bool HasUnresolvedThreads = false)
{
    public string Key => $"{Repo}#{Number}";

    /// <summary>
    /// Whether this is the same PR state as <paramref name="other"/>, for deciding a poll changed nothing.
    /// <para>
    /// Record equality cannot answer this: the three list members are rebuilt from JSON on every poll and
    /// compare by reference, so `==` is always false. Hence an explicit list — and the rule for extending it
    /// lives here, next to the fields: <b>a field the UI reads that GitHub can change without stamping
    /// <see cref="UpdatedAt"/> must be named below</b>, or the snapshot short-circuits and the UI goes stale
    /// with nothing failing. Checks, mergeability and thread resolution are all such fields. The branches are
    /// named, and so are the standing reviews, so a snapshot written before they were fetched is replaced on the
    /// first poll; re-requesting a review also leaves a standing change request in place.
    /// </para>
    /// </summary>
    public bool SameStateAs(PullRequest other) =>
        UpdatedAt == other.UpdatedAt
        && Checks == other.Checks
        && Mergeable == other.Mergeable
        && HasOpenThreads == other.HasOpenThreads
        && HasUnresolvedThreads == other.HasUnresolvedThreads
        && BaseBranch == other.BaseBranch
        && HeadBranch == other.HeadBranch
        && DefaultBranch == other.DefaultBranch
        && StandingReviews.SequenceEqual(other.StandingReviews);

    /// <summary>Whether the PR merges somewhere other than its repository's default branch, such as a release
    /// branch or another PR's branch. False when the default is unknown.</summary>
    public bool TargetsNonDefaultBranch =>
        BaseBranch.Length > 0 && DefaultBranch.Length > 0 && !string.Equals(BaseBranch, DefaultBranch, StringComparison.Ordinal);

    /// <summary>
    /// Each reviewer's approval or change request that still stands, change requests first. A reviewer asked
    /// again drops out of <see cref="LatestReviews"/> but their verdict stays in force, so both lists are merged
    /// and the newest verdict per reviewer wins.
    /// </summary>
    public IReadOnlyList<Review> Verdicts() =>
        [.. LatestReviews.Concat(StandingReviews)
            .Where(r => r.State is ReviewState.Approved or ReviewState.ChangesRequested)
            .GroupBy(r => r.Author, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.MaxBy(r => r.SubmittedAt)!)
            .OrderBy(r => r.State == ReviewState.Approved)
            .ThenBy(r => r.Author, StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// The review decision to act on. GitHub's <see cref="ReviewDecision"/> wins when it has one. It is null on
    /// some PRs that carry a standing approval (renewedvision/friday#17598, approved by CodeRabbit on its head
    /// commit), so the decision is then read from the verdicts the review chips show: any change request
    /// outweighs approvals, as on GitHub.
    /// </summary>
    public ReviewDecision? EffectiveReviewDecision
    {
        get
        {
            if (ReviewDecision is { } decision)
            {
                return decision;
            }

            var verdicts = Verdicts();
            return verdicts.Any(r => r.State == ReviewState.ChangesRequested) ? Models.ReviewDecision.ChangesRequested
                : verdicts.Count > 0 ? Models.ReviewDecision.Approved
                : null;
        }
    }

    // Snapshots written by an older build lack newer collection fields; they must read as empty, not null.
    public IReadOnlyList<string> Labels { get; init; } = Labels ?? [];
    public IReadOnlyList<string> RequestedReviewers { get; init; } = RequestedReviewers ?? [];
    public IReadOnlyList<Review> LatestReviews { get; init; } = LatestReviews ?? [];

    /// <summary>
    /// Each reviewer's last approval or change request that still stands. <see cref="LatestReviews"/> drops a
    /// reviewer once their review is requested again, yet GitHub keeps their change request blocking until they
    /// review again or it is dismissed, so the verdict shown has to come from here.
    /// </summary>
    public IReadOnlyList<Review> StandingReviews { get; init; } = StandingReviews ?? [];
    public string BaseBranch { get; init; } = BaseBranch ?? string.Empty;
    public string HeadBranch { get; init; } = HeadBranch ?? string.Empty;
    public string DefaultBranch { get; init; } = DefaultBranch ?? string.Empty;
}
