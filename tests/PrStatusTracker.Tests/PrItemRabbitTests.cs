using PrStatusTracker.Core;
using PrStatusTracker.Models;
using PrStatusTracker.ViewModels;
using Xunit;
using static PrStatusTracker.Tests.TestData;

namespace PrStatusTracker.Tests;

public class PrItemRabbitTests
{
    private const string Rabbit = "coderabbitai";

    private static PrItemViewModel Item(IReadOnlyList<Review>? reviews = null, IReadOnlyList<string>? requested = null) =>
        new(new TrackedPr(Pr(author: Me, reviews: reviews, requested: requested), PrStatus.AwaitingReview), isStarred: false);

    private static Review By(string author, ReviewState state, int minutes = 0) => new(author, state, T0.AddMinutes(minutes));

    [Fact]
    public void A_rabbit_approval_is_not_counted_as_a_human_one()
    {
        var item = Item([By(Rabbit, ReviewState.Approved), By("bob", ReviewState.Approved)]);
        Assert.Equal("1 approved", item.Reviews);
        Assert.Equal(ReviewVerdict.Approved, item.Rabbit);
        Assert.Equal("CodeRabbit approved", item.RabbitTip);
    }

    [Fact]
    public void Only_a_rabbit_approval_leaves_the_human_tally_empty()
    {
        var item = Item([By(Rabbit, ReviewState.Approved)]);
        Assert.Equal(string.Empty, item.Reviews);
        Assert.True(item.HasRabbit);
    }

    [Fact]
    public void The_rabbits_latest_review_wins()
    {
        var item = Item([By(Rabbit, ReviewState.ChangesRequested, minutes: 0), By("coderabbitai[bot]", ReviewState.Approved, minutes: 5)]);
        Assert.Equal(ReviewVerdict.Approved, item.Rabbit);
    }

    [Fact]
    public void A_requested_rabbit_that_has_not_reviewed_is_pending_and_not_a_human_awaiting()
    {
        var item = Item(requested: [Rabbit, "bob"]);
        Assert.Equal(ReviewVerdict.Pending, item.Rabbit);
        Assert.Equal("1 awaiting", item.Reviews);
    }

    [Fact]
    public void No_rabbit_means_no_chip()
    {
        var item = Item([By("bob", ReviewState.ChangesRequested)]);
        Assert.False(item.HasRabbit);
        Assert.Equal("changes requested by bob", item.Reviews);
    }
}
