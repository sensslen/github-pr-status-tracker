using System.Text.Json;
using System.Text.Json.Nodes;
using PrStatusTracker.GitHub;
using PrStatusTracker.Models;
using Xunit;
using static PrStatusTracker.Tests.TestData;

namespace PrStatusTracker.Tests;

public class GitHubClientParseTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public async Task Parses_both_lists_and_dedupes_with_authored_precedence()
    {
        var calls = new List<IReadOnlyList<string>>();
        var client = new GitHubClient((args, ct) =>
        {
            calls.Add(args);
            return Task.FromResult(new GhResult(0, args switch
            {
                _ when args.Any(a => a.StartsWith("authoredQ=", StringComparison.Ordinal)) => Fixture("graphql-response.json"),
                _ when args.Contains("ids[]=PR_7") && args.Contains("ids[]=PR_42") && !args.Contains("ids[]=PR_101") =>
                    Page(Pr("PR_7", Thread(resolved: false, "carol")), Pr("PR_42", Thread(resolved: false, "owen"))),
                _ => throw new InvalidOperationException("Only authored PRs have their threads checked, in one request"),
            }, ""));
        });

        var prs = await client.FetchPullRequestsAsync(Me);

        Assert.Equal(3, prs.Count);
        Assert.Equal(2, calls.Count);

        var review = Assert.Single(prs, p => p.Number == 101);
        Assert.Equal("acme/widgets", review.Repo);
        Assert.Equal("alice", review.Author);
        Assert.Equal(ReviewDecision.ReviewRequired, review.ReviewDecision);
        Assert.Equal(Mergeable.Mergeable, review.Mergeable);
        Assert.Equal(["owen", "platform-team"], review.RequestedReviewers);
        Assert.Equal(["bug", "backend"], review.Labels);
        Assert.Equal(CheckState.Pending, review.Checks);
        Assert.Equal(new DateTimeOffset(2026, 9, 14, 9, 0, 0, TimeSpan.Zero), review.HeadCommittedAt);
        Assert.False(review.HasOpenThreads);

        var authored = Assert.Single(prs, p => p.Number == 7);
        Assert.Equal("owen", authored.Author);
        Assert.Empty(authored.Labels);
        Assert.Equal(ReviewDecision.Approved, authored.ReviewDecision);
        Assert.Equal(CheckState.Success, authored.Checks);
        Assert.Equal((120, 34), (authored.Additions, authored.Deletions));
        Assert.Equal(new DateTimeOffset(2026, 9, 12, 8, 0, 0, TimeSpan.Zero), authored.CreatedAt);
        var r = Assert.Single(authored.LatestReviews);
        Assert.Equal(("bob", ReviewState.Approved), (r.Author, r.State));
        Assert.True(authored.HasOpenThreads);

        // #42 appears in both search results; the authored copy (with the newer title) wins.
        var both = Assert.Single(prs, p => p.Number == 42);
        Assert.Equal("Authored copy", both.Title);
        Assert.Null(both.Checks);
        Assert.Null(both.ReviewDecision);
        Assert.Equal(Mergeable.Conflicting, both.Mergeable);
        Assert.True(both.IsDraft);
        Assert.False(both.HasOpenThreads);
    }

    private static string Search(string updatedAt = "2026-09-21T09:00:00Z") => $$"""
        { "data": { "authored": { "nodes": [ {
          "id": "PR_1", "number": 1, "url": "u", "title": "t", "isDraft": false, "updatedAt": "{{updatedAt}}",
          "author": { "login": "{{Me}}" }, "repository": { "nameWithOwner": "acme/widgets" },
          "mergeable": "MERGEABLE",
          "latestReviews": { "nodes": [] },
          "commits": { "nodes": [] }
        } ] } } }
        """;

    private static string Threads(string threads, string? nextCursor = null) => Page(Pr("PR_1", threads, nextCursor));

    private static string Page(params string[] prs) => $$"""{ "data": { "nodes": [ {{string.Join(",", prs)}} ] } }""";

    private static string Pr(string id, string threads, string? nextCursor = null) => $$"""
        { "id": "{{id}}", "reviewThreads": {
          "pageInfo": { "hasNextPage": {{(nextCursor is null ? "false" : "true")}}, "endCursor": {{(nextCursor is null ? "null" : $"\"{nextCursor}\"")}} },
          "nodes": [ {{threads}} ]
        } }
        """;

    private static string Thread(bool resolved, string login, string? pendingReplyBy = null)
    {
        var pending = pendingReplyBy is null ? "" : $$""", { "author": { "login": "{{pendingReplyBy}}" }, "state": "PENDING" }""";
        return $$"""
            { "isResolved": {{(resolved ? "true" : "false")}}, "comments": { "nodes": [
              { "author": { "login": "{{login}}" }, "state": "SUBMITTED" }{{pending}}
            ] } }
            """;
    }

    /// <summary>Answers the search with <paramref name="searches"/> and each thread query with the next of <paramref name="pages"/>.</summary>
    private static GitHubClient Client(List<IReadOnlyList<string>> calls, Queue<string> searches, Queue<string> pages) =>
        new((args, ct) =>
        {
            calls.Add(args);
            var search = args.Any(a => a.StartsWith("authoredQ=", StringComparison.Ordinal));
            return Task.FromResult(new GhResult(0, search ? searches.Dequeue() : pages.Dequeue(), ""));
        });

    private static async Task<PullRequest> Fetch(params string[] pages)
    {
        var client = Client([], new Queue<string>([Search()]), new Queue<string>(pages));
        return Assert.Single(await client.FetchPullRequestsAsync(Me));
    }

    [Fact]
    public async Task A_conversation_you_have_resolved_is_not_waiting_on_you()
    {
        var pr = await Fetch(Threads(string.Join(",", Thread(resolved: true, "alice"), Thread(resolved: true, "bob"))));

        Assert.False(pr.HasOpenThreads);
    }

    [Fact]
    public async Task An_open_thread_still_counts_even_beside_resolved_ones()
    {
        var pr = await Fetch(Threads(string.Join(",", Thread(resolved: true, "alice"), Thread(resolved: false, "bob"))));

        Assert.True(pr.HasOpenThreads);
    }

    [Theory]
    [InlineData("owen", false)]
    [InlineData("OWEN", false)]
    [InlineData("alice", true)]
    [InlineData("coderabbitai[bot]", true)]
    public async Task Open_threads_awaiting_the_author_count_including_bots(string login, bool expected)
    {
        var pr = await Fetch(Threads(Thread(resolved: false, login)));

        Assert.Equal(expected, pr.HasOpenThreads);
    }

    [Fact]
    public async Task An_author_reply_does_not_hide_another_actionable_thread()
    {
        var pr = await Fetch(Threads(string.Join(",", Thread(resolved: false, Other), Thread(resolved: false, Me))));

        Assert.True(pr.HasOpenThreads);
    }

    [Fact]
    public async Task A_pending_reply_leaves_the_thread_waiting_on_you()
    {
        var pr = await Fetch(Threads(Thread(resolved: false, Other, pendingReplyBy: Me)));

        Assert.True(pr.HasOpenThreads);
    }

    [Theory]
    [InlineData("""{"data":{"nodes":[null]}}""")]
    [InlineData("""{"data":{"nodes":[{"id":"PR_1"}]}}""")]
    public async Task Missing_threads_fail_the_poll_rather_than_reading_as_quiet(string page)
    {
        var client = Client([], new Queue<string>([Search()]), new Queue<string>([page]));

        await Assert.ThrowsAsync<JsonException>(() => client.FetchPullRequestsAsync(Me));
    }

    public class Reuse
    {
        [Fact]
        public async Task A_quiet_pr_is_not_asked_again_until_it_changes()
        {
            var calls = new List<IReadOnlyList<string>>();
            var client = Client(calls,
                new Queue<string>([Search("2026-09-21T09:00:00Z"), Search("2026-09-21T09:00:00Z"), Search("2026-09-21T10:00:00Z")]),
                new Queue<string>([Threads(Thread(resolved: true, Other)), Threads(Thread(resolved: false, Other))]));

            Assert.False(Assert.Single(await client.FetchPullRequestsAsync(Me)).HasOpenThreads);
            Assert.False(Assert.Single(await client.FetchPullRequestsAsync(Me)).HasOpenThreads);
            Assert.Equal(3, calls.Count);
            Assert.True(Assert.Single(await client.FetchPullRequestsAsync(Me)).HasOpenThreads);
            Assert.Equal(5, calls.Count);
        }

        [Fact]
        public async Task Resolving_a_thread_clears_it_without_new_activity()
        {
            var client = Client([], new Queue<string>([Search(), Search()]),
                new Queue<string>([Threads(Thread(resolved: false, Other)), Threads(Thread(resolved: true, Other))]));

            Assert.True(Assert.Single(await client.FetchPullRequestsAsync(Me)).HasOpenThreads);
            Assert.False(Assert.Single(await client.FetchPullRequestsAsync(Me)).HasOpenThreads);
        }

        [Fact]
        public async Task Reopening_a_thread_brings_it_back_once_the_quiet_answer_expires()
        {
            var calls = new List<IReadOnlyList<string>>();
            var client = Client(calls, new Queue<string>(Enumerable.Repeat(Search(), GitHubClient.QuietReuses + 2)),
                new Queue<string>([Threads(Thread(resolved: true, Other)), Threads(Thread(resolved: false, Other))]));

            for (var poll = 0; poll <= GitHubClient.QuietReuses; poll++)
            {
                Assert.False(Assert.Single(await client.FetchPullRequestsAsync(Me)).HasOpenThreads);
            }

            Assert.True(Assert.Single(await client.FetchPullRequestsAsync(Me)).HasOpenThreads);
            Assert.Equal(GitHubClient.QuietReuses + 4, calls.Count);
        }
    }

    public class Paging
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Fetches_later_thread_pages_before_claiming_there_is_no_work(bool actionable)
        {
            var calls = new List<IReadOnlyList<string>>();
            var client = Client(calls, new Queue<string>([Search()]), new Queue<string>(
            [
                Threads(string.Join(",", Enumerable.Repeat(Thread(resolved: true, Other), 50)), nextCursor: "page1"),
                Threads("", nextCursor: "page2"),
                Threads(Thread(!actionable, Other)),
            ]));

            var pr = Assert.Single(await client.FetchPullRequestsAsync(Me));

            Assert.Equal(actionable, pr.HasOpenThreads);
            Assert.Equal(4, calls.Count);
            Assert.Contains("ids[]=PR_1", calls[1]);
            Assert.DoesNotContain(calls[1], a => a.StartsWith("cursor=", StringComparison.Ordinal));
            Assert.Contains("cursor=page1", calls[2]);
            Assert.Contains("cursor=page2", calls[3]);
        }

        [Fact]
        public async Task Stops_paging_once_an_actionable_thread_is_found()
        {
            var calls = new List<IReadOnlyList<string>>();
            var client = Client(calls, new Queue<string>([Search()]),
                new Queue<string>([Threads(Thread(resolved: false, Other), nextCursor: "page1")]));

            Assert.True(Assert.Single(await client.FetchPullRequestsAsync(Me)).HasOpenThreads);
            Assert.Equal(2, calls.Count);
        }

        [Fact]
        public async Task A_failed_thread_page_fails_the_poll()
        {
            var client = Client([], new Queue<string>([Search()]),
                new Queue<string>([Threads("", nextCursor: "page1"), """{"errors":[{"message":"boom"}]}"""]));

            await Assert.ThrowsAsync<GhException>(() => client.FetchPullRequestsAsync(Me));
        }
    }

    [Fact]
    public void Surfaces_graphql_errors()
    {
        var ex = Assert.Throws<GhException>(() => GitHubClient.ParseResponse("""{"errors":[{"message":"boom"}]}"""));
        Assert.Contains("boom", ex.Message);
    }
}
