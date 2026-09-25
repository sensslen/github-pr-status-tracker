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
    bool HasOpenThreads = false)
{
    public string Key => $"{Repo}#{Number}";

    /// <summary>
    /// Whether this is the same PR state as <paramref name="other"/>, for deciding a poll changed nothing.
    /// <para>
    /// Record equality cannot answer this: the three list members are rebuilt from JSON on every poll and
    /// compare by reference, so `==` is always false. Hence an explicit list — and the rule for extending it
    /// lives here, next to the fields: <b>a field the UI reads that GitHub can change without stamping
    /// <see cref="UpdatedAt"/> must be named below</b>, or the snapshot short-circuits and the UI goes stale
    /// with nothing failing. Checks, mergeability and thread resolution are all such fields.
    /// </para>
    /// </summary>
    public bool SameStateAs(PullRequest other) =>
        UpdatedAt == other.UpdatedAt
        && Checks == other.Checks
        && Mergeable == other.Mergeable
        && HasOpenThreads == other.HasOpenThreads;

    // Snapshots written by an older build lack newer collection fields; they must read as empty, not null.
    public IReadOnlyList<string> Labels { get; init; } = Labels ?? [];
    public IReadOnlyList<string> RequestedReviewers { get; init; } = RequestedReviewers ?? [];
    public IReadOnlyList<Review> LatestReviews { get; init; } = LatestReviews ?? [];
}
