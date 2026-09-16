using PrStatusTracker.GitHub;
using PrStatusTracker.Models;
using Xunit;

namespace PrStatusTracker.Tests;

public class GitHubClientParseTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void Parses_both_lists_and_dedupes_with_authored_precedence()
    {
        var prs = GitHubClient.ParseResponse(Fixture("graphql-response.json"));

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

        var authored = Assert.Single(prs, p => p.Number == 7);
        Assert.Equal("owen", authored.Author);
        Assert.Empty(authored.Labels);
        Assert.Equal(ReviewDecision.Approved, authored.ReviewDecision);
        Assert.Equal(CheckState.Success, authored.Checks);
        Assert.Equal((120, 34), (authored.Additions, authored.Deletions));
        Assert.Equal(new DateTimeOffset(2026, 9, 12, 8, 0, 0, TimeSpan.Zero), authored.CreatedAt);
        var r = Assert.Single(authored.LatestReviews);
        Assert.Equal(("bob", ReviewState.Approved), (r.Author, r.State));

        // #42 appears in both search results; the authored copy (with the newer title) wins.
        var both = Assert.Single(prs, p => p.Number == 42);
        Assert.Equal("Authored copy", both.Title);
        Assert.Null(both.Checks);
        Assert.Null(both.ReviewDecision);
        Assert.Equal(Mergeable.Conflicting, both.Mergeable);
        Assert.True(both.IsDraft);
    }

    [Fact]
    public void Surfaces_graphql_errors()
    {
        var ex = Assert.Throws<GhException>(() => GitHubClient.ParseResponse("""{"errors":[{"message":"boom"}]}"""));
        Assert.Contains("boom", ex.Message);
    }
}
