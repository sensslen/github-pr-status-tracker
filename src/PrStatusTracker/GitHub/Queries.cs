namespace PrStatusTracker.GitHub;

internal static class Queries
{
    // Three searches in one round trip. `review-requested:` drops a PR the moment you submit a review,
    // so `reviewed-by:` is needed to keep following PRs you have already approved or requested changes on.
    //
    // Comment activity is asked for on the authored search alone: only your own PRs track comments as read or
    // unread, so paying for these three times over would fill a column nothing reads.
    //
    // It is asked for as threads rather than as `latestReviews`, which is already above and would be free,
    // because only a thread carries `isResolved` — and a conversation you have settled is not something still
    // waiting on you. `latestReviews` would drag every resolved thread's last reply back in.
    public const string PullRequests = """
        query($reviewQ: String!, $reviewedQ: String!, $authoredQ: String!) {
          review:   search(query: $reviewQ,   type: ISSUE, first: 50) { nodes { ...PrFields } }
          reviewed: search(query: $reviewedQ, type: ISSUE, first: 50) { nodes { ...PrFields } }
          authored: search(query: $authoredQ, type: ISSUE, first: 50) { nodes { ...PrFields ...CommentFields } }
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

        fragment CommentFields on PullRequest {
          comments(last: 1) { nodes { author { login } createdAt } }
          reviewThreads(last: 20) { nodes {
            isResolved
            comments(last: 1) { nodes { author { login } createdAt } }
          } }
          reviews(last: 5) { nodes { author { login } createdAt bodyText comments { totalCount } } }
        }
        """;

    /// <summary>
    /// Search aliases in the query above, in precedence order (later wins when a PR appears twice), each paired
    /// with whether its nodes carry <c>CommentFields</c>. That pairing is declared here, beside the document
    /// that decides it, so the parser is told which searches track comments rather than inferring it from
    /// which fields happen to have come back.
    /// </summary>
    public static readonly (string Name, bool TracksComments)[] Aliases =
        [("review", false), ("reviewed", false), ("authored", true)];

    public static string ReviewRequested(string login) => $"is:open is:pr review-requested:{login} archived:false";
    public static string ReviewedBy(string login) => $"is:open is:pr reviewed-by:{login} -author:{login} archived:false";
    public static string Authored(string login) => $"is:open is:pr author:{login} archived:false";
}
