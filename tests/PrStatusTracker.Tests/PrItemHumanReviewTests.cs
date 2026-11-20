using PrStatusTracker.Core;
using PrStatusTracker.Models;
using PrStatusTracker.ViewModels;
using Xunit;
using static PrStatusTracker.Tests.TestData;

namespace PrStatusTracker.Tests;

public class PrItemHumanReviewTests
{
    private static PrItemViewModel Item(IReadOnlyList<Review>? latest = null, IReadOnlyList<Review>? standing = null,
        IReadOnlyList<string>? requested = null, ReviewDecision? decision = null) =>
        new(new TrackedPr(Pr(author: Me, reviews: latest, requested: requested, decision: decision) with { StandingReviews = standing ?? [] },
            PrStatus.AwaitingReview), isStarred: false);

    private static Review By(string author, ReviewState state, int minutes = 0) => new(author, state, T0.AddMinutes(minutes));

    [Fact]
    public void A_change_request_from_a_reviewer_asked_again_still_shows()
    {
        // GitHub drops the reviewer from latestReviews once asked again, but their change request keeps blocking.
        var item = Item(latest: [By("coderabbitai", ReviewState.Commented)],
            standing: [By("stetson", ReviewState.ChangesRequested)], requested: ["stetson", "dana"],
            decision: ReviewDecision.ChangesRequested);

        Assert.Equal(ReviewVerdict.ChangesRequested, item.Human);
        Assert.Equal("changes requested by stetson, 2 awaiting", item.Reviews);
        Assert.Equal("stetson requested changes\nAsked again: stetson\nWaiting on: dana", item.HumanTip);
    }

    [Fact]
    public void A_change_request_outweighs_approvals() =>
        Assert.Equal(ReviewVerdict.ChangesRequested,
            Item(latest: [By("bob", ReviewState.Approved), By("carol", ReviewState.ChangesRequested)]).Human);

    [Fact]
    public void A_newer_approval_replaces_a_standing_change_request()
    {
        var item = Item(latest: [By("bob", ReviewState.Approved, minutes: 5)], standing: [By("bob", ReviewState.ChangesRequested)],
            decision: ReviewDecision.Approved);
        Assert.Equal(ReviewVerdict.Approved, item.Human);
        Assert.Equal(1, item.HumanApprovals);
    }

    [Fact]
    public void An_approval_github_still_wants_more_reviews_for_is_pending() =>
        Assert.Equal(ReviewVerdict.Pending,
            Item(latest: [By("bob", ReviewState.Approved)], requested: ["carol"], decision: ReviewDecision.ReviewRequired).Human);

    [Fact]
    public void Only_comments_from_people_read_as_commented() =>
        Assert.Equal(ReviewVerdict.Commented, Item(latest: [By("bob", ReviewState.Commented)]).Human);

    [Fact]
    public void No_people_involved_reads_as_none()
    {
        var item = Item(latest: [By("coderabbitai", ReviewState.Approved)], requested: ["coderabbitai"]);
        Assert.Equal(ReviewVerdict.None, item.Human);
        Assert.Equal("No reviewers yet", item.HumanTip);
    }
}
