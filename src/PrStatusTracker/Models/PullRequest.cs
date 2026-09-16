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
    CheckState? Checks)
{
    public string Key => $"{Repo}#{Number}";

    // Snapshots written by an older build lack newer collection fields; they must read as empty, not null.
    public IReadOnlyList<string> Labels { get; init; } = Labels ?? [];
    public IReadOnlyList<string> RequestedReviewers { get; init; } = RequestedReviewers ?? [];
    public IReadOnlyList<Review> LatestReviews { get; init; } = LatestReviews ?? [];
}
