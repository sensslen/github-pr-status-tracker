namespace PrStatusTracker.GitHub;

internal static class Queries
{
    // Three searches in one round trip. `review-requested:` drops a PR the moment you submit a review,
    // so `reviewed-by:` is needed to keep following PRs you have already approved or requested changes on.
    public const string PullRequests = """
        query($reviewQ: String!, $reviewedQ: String!, $authoredQ: String!) {
          review:   search(query: $reviewQ,   type: ISSUE, first: 50) { nodes { ...PrFields } }
          reviewed: search(query: $reviewedQ, type: ISSUE, first: 50) { nodes { ...PrFields } }
          authored: search(query: $authoredQ, type: ISSUE, first: 50) { nodes { ...PrFields } }
        }

        fragment PrFields on PullRequest {
          number title url isDraft createdAt updatedAt additions deletions
          author { login }
          repository { nameWithOwner }
          labels(first: 10) { nodes { name } }
          reviewDecision
          mergeable
          reviewRequests(first: 20) { nodes { requestedReviewer {
            ... on User { login }
            ... on Team { name }
          } } }
          latestReviews(first: 20) { nodes { author { login } state submittedAt } }
          commits(last: 1) { nodes { commit {
            committedDate
            statusCheckRollup { state }
          } } }
        }
        """;

    /// <summary>Search aliases in the query above, in precedence order (later wins when a PR appears twice).</summary>
    public static readonly string[] Aliases = ["review", "reviewed", "authored"];

    public static string ReviewRequested(string login) => $"is:open is:pr review-requested:{login} archived:false";
    public static string ReviewedBy(string login) => $"is:open is:pr reviewed-by:{login} -author:{login} archived:false";
    public static string Authored(string login) => $"is:open is:pr author:{login} archived:false";
}
