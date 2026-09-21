using PrStatusTracker.GitHub;
using PrStatusTracker.Models;
using Xunit;
using static PrStatusTracker.Tests.TestData;

namespace PrStatusTracker.Tests;

public class GitHubClientParseTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void Parses_both_lists_and_dedupes_with_authored_precedence()
    {
        var prs = GitHubClient.ParseResponse(Fixture("graphql-response.json"), Me);

        Assert.Equal(3, prs.Count);

        var review = Assert.Single(prs, p => p.Number == 101);
        Assert.Equal("acme/widgets", review.Repo);
        Assert.Equal("alice", review.Author);
        Assert.Equal(ReviewDecision.ReviewRequired, review.ReviewDecision);
        Assert.Equal(Mergeable.Mergeable, review.Mergeable);
        Assert.Equal(["owen", "platform-team"], review.RequestedReviewers);
        Assert.Equal(["bug", "backend"], review.Labels);
        Assert.Equal(CheckState.Pending, review.Checks);
        Assert.Equal(new DateTimeOffset(2026, 9, 14, 9, 0, 0, TimeSpan.Zero), review.HeadCommittedAt);
        // Comment activity is only asked for on the authored search, so it is absent from a PR you review.
        Assert.Null(review.LastCommentAt);

        var authored = Assert.Single(prs, p => p.Number == 7);
        Assert.Equal("owen", authored.Author);
        Assert.Empty(authored.Labels);
        Assert.Equal(ReviewDecision.Approved, authored.ReviewDecision);
        Assert.Equal(CheckState.Success, authored.Checks);
        Assert.Equal((120, 34), (authored.Additions, authored.Deletions));
        Assert.Equal(new DateTimeOffset(2026, 9, 12, 8, 0, 0, TimeSpan.Zero), authored.CreatedAt);
        var r = Assert.Single(authored.LatestReviews);
        Assert.Equal(("bob", ReviewState.Approved), (r.Author, r.State));
        // Newest of the comment and review connections wins: bob's comment is 15 minutes after carol's review.
        Assert.Equal(new DateTimeOffset(2026, 9, 15, 9, 45, 0, TimeSpan.Zero), authored.LastCommentAt);
        Assert.Equal("bob", authored.LastCommentBy);

        // #42 appears in both search results; the authored copy (with the newer title) wins.
        var both = Assert.Single(prs, p => p.Number == 42);
        Assert.Equal("Authored copy", both.Title);
        Assert.Null(both.Checks);
        Assert.Null(both.ReviewDecision);
        Assert.Equal(Mergeable.Conflicting, both.Mergeable);
        Assert.True(both.IsDraft);
        // You had the last word here, so there is nothing on it you have not read.
        Assert.Null(both.LastCommentAt);
    }

    /// <summary>
    /// One authored PR, with the three sources of comment activity written out so a test can say which of
    /// them should win. Threads carry the resolution; issue comments and standalone review bodies cannot be
    /// resolved at all.
    /// </summary>
    private static string AuthoredPr(string threads, string reviews, string comments = """
        { "author": { "login": "alice" }, "createdAt": "2026-09-21T09:00:00Z" }
        """) => $$"""
        { "data": { "authored": { "nodes": [ {
          "number": 1, "url": "u", "title": "t", "isDraft": false,
          "author": { "login": "{{Me}}" }, "repository": { "nameWithOwner": "acme/widgets" },
          "mergeable": "MERGEABLE",
          "latestReviews": { "nodes": [] },
          "commits": { "nodes": [] },
          "comments": { "nodes": [ {{comments}} ] },
          "reviewThreads": { "nodes": [ {{threads}} ] },
          "reviews": { "nodes": [ {{reviews}} ] }
        } ] } } }
        """;

    private static string Thread(bool resolved, string login, string at) => $$"""
        { "isResolved": {{(resolved ? "true" : "false")}},
          "comments": { "nodes": [ { "author": { "login": "{{login}}" }, "createdAt": "{{at}}" } ] } }
        """;

    private static string Review(string login, string at, string body, int inlineComments) => $$"""
        { "author": { "login": "{{login}}" }, "createdAt": "{{at}}",
          "bodyText": "{{body}}", "comments": { "totalCount": {{inlineComments}} } }
        """;

    private static PullRequest Parse(string json) => Assert.Single(GitHubClient.ParseResponse(json, Me));

    /// <summary>
    /// The case that prompted this: every thread resolved, so the PR is settled even though the reviews that
    /// carried those threads are recent. Reporting them made a quiet PR claim it wanted something.
    /// </summary>
    [Fact]
    public void A_conversation_you_have_resolved_is_not_waiting_on_you()
    {
        var pr = Parse(AuthoredPr(
            threads: string.Join(",", Thread(resolved: true, "alice", "2026-09-21T12:00:00Z"),
                                      Thread(resolved: true, "bob", "2026-09-21T13:00:00Z")),
            reviews: Review("alice", "2026-09-21T13:00:00Z", "Actionable comments posted: 1", inlineComments: 1),
            comments: ""));

        Assert.Null(pr.LastCommentAt);
    }

    [Fact]
    public void An_open_thread_still_counts_even_beside_resolved_ones()
    {
        var pr = Parse(AuthoredPr(
            threads: string.Join(",", Thread(resolved: true, "alice", "2026-09-21T14:00:00Z"),
                                      Thread(resolved: false, "bob", "2026-09-21T12:00:00Z")),
            reviews: "",
            comments: ""));

        // The resolved thread is newer, and is still ignored: what matters is the newest thing left open.
        Assert.Equal(new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero), pr.LastCommentAt);
        Assert.Equal("bob", pr.LastCommentBy);
    }

    /// <summary>A body posted with inline comments summarises them; the thread is what carries resolution.</summary>
    [Theory]
    [InlineData(1, null)]   // alongside inline comments: resolved with them
    [InlineData(0, "bob")]  // standalone commentary: nothing to resolve, so it stands
    public void A_review_body_counts_only_when_it_is_standalone(int inlineComments, string? expectedBy)
    {
        var pr = Parse(AuthoredPr(
            threads: Thread(resolved: true, "bob", "2026-09-21T12:00:00Z"),
            reviews: Review("bob", "2026-09-21T13:00:00Z", "Please rename this", inlineComments),
            comments: ""));

        Assert.Equal(expectedBy, pr.LastCommentBy);
    }

    [Fact]
    public void An_issue_comment_can_never_be_resolved_so_it_always_counts()
    {
        var pr = Parse(AuthoredPr(threads: Thread(resolved: true, "bob", "2026-09-21T13:00:00Z"), reviews: ""));

        Assert.Equal("alice", pr.LastCommentBy);
    }

    /// <summary>Whichever source carries it, your own last word means there is nothing left unread.</summary>
    [Fact]
    public void Your_own_reply_in_an_open_thread_still_suppresses_it()
    {
        var pr = Parse(AuthoredPr(
            threads: Thread(resolved: false, Me, "2026-09-21T13:00:00Z"),
            reviews: "",
            comments: ""));

        Assert.Null(pr.LastCommentAt);
    }

    [Fact]
    public void Surfaces_graphql_errors()
    {
        var ex = Assert.Throws<GhException>(() => GitHubClient.ParseResponse("""{"errors":[{"message":"boom"}]}""", Me));
        Assert.Contains("boom", ex.Message);
    }
}
