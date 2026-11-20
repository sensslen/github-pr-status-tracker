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
          id number title url isDraft createdAt updatedAt additions deletions
          author { login }
          baseRefName headRefName
          repository { nameWithOwner defaultBranchRef { name } }
          labels(first: 10) { nodes { name } }
          reviewDecision
          mergeable
          reviewRequests(first: 20) { nodes { requestedReviewer {
            ... on User { login }
            ... on Team { name }
          } } }
          latestReviews(first: 20) { nodes { author { login } state submittedAt } }
          latestOpinionatedReviews(first: 20) { nodes { author { login } state submittedAt } }
          commits(last: 1) { nodes { commit {
            committedDate
            statusCheckRollup { state }
          } } }
        }
        """;

    // A search result's nested connections are charged for every PR it returns, so threads are fetched only for
    // the PRs you authored, all in one request. Your own pending replies come
    // back too, hence `last: 5` to see past them to the last submitted comment.
    public const string ReviewThreads = """
        query($ids: [ID!]!, $cursor: String) {
          nodes(ids: $ids) { ... on PullRequest {
            id
            reviewThreads(first: 50, after: $cursor) {
              pageInfo { hasNextPage endCursor }
              nodes {
                isResolved
                comments(last: 5) { nodes { author { login } state } }
              }
            }
          } }
        }
        """;

    /// <summary>Search aliases in the query above, in precedence order (later wins when a PR appears twice).</summary>
    public static readonly string[] Aliases = ["review", "reviewed", AuthoredAlias];

    /// <summary>The alias whose PRs get their review threads checked.</summary>
    public const string AuthoredAlias = "authored";

    public static string ReviewRequested(string login) => $"is:open is:pr review-requested:{login} archived:false";
    public static string ReviewedBy(string login) => $"is:open is:pr reviewed-by:{login} -author:{login} archived:false";
    public static string Authored(string login) => $"is:open is:pr author:{login} archived:false";
}
