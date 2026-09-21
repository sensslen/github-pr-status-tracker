using PrStatusTracker.Models;
using PrStatusTracker.ViewModels;
using Xunit;
using static PrStatusTracker.Tests.TestData;

namespace PrStatusTracker.Tests;

/// <summary>What a row in the Your PRs tab is calling for, and how the strip above it adds those up.</summary>
public class PrItemCalloutTests
{
    private static PrItemViewModel Item(PrStatus status, bool newComments = false, int number = 1) =>
        new(Tracked(status, number: number, author: Me), isStarred: false, newComments);

    [Fact]
    public void A_quiet_pr_calls_for_nothing()
    {
        Assert.Empty(PrStatus.AwaitingReview.Callouts(hasNewComments: false));

        var item = Item(PrStatus.AwaitingReview);
        Assert.False(item.NeedsAttention);
        Assert.Equal(PrCallout.None, item.Callout);
    }

    [Fact]
    public void New_comments_alone_are_worth_calling_out()
    {
        Assert.Equal([PrCallout.NewComments], PrStatus.AwaitingReview.Callouts(hasNewComments: true));
        Assert.True(Item(PrStatus.AwaitingReview, newComments: true).NeedsAttention);
    }

    [Fact]
    public void The_status_leads_when_a_pr_is_both_broken_and_unread()
    {
        Assert.Equal([PrCallout.ChecksFailing, PrCallout.NewComments], PrStatus.ChecksFailing.Callouts(hasNewComments: true));
        Assert.Equal(PrCallout.ChecksFailing, Item(PrStatus.ChecksFailing, newComments: true).Callout);
    }

    /// <summary>
    /// The pill and the attention strip have to agree. A PR whose pill read "Awaiting review" while the strip
    /// counted it as wanting something was saying two different things about the same row.
    /// </summary>
    [Fact]
    public void A_quiet_pr_with_unread_comments_says_so_on_its_pill()
    {
        Assert.Equal("Awaiting review", Item(PrStatus.AwaitingReview).StatusLabel);
        Assert.Equal("New comments", Item(PrStatus.AwaitingReview, newComments: true).StatusLabel);
    }

    /// <summary>Where the status already wants something, that leads and the 💬 marker carries the rest.</summary>
    [Fact]
    public void A_status_that_wants_something_keeps_its_own_pill()
    {
        var item = Item(PrStatus.ChecksFailing, newComments: true);

        Assert.Equal("Checks failing", item.StatusLabel);
        Assert.True(item.HasNewComments);
    }

    [Fact]
    public void The_strip_counts_every_callout_a_row_carries()
    {
        var summary = PrGroupViewModel.Summarise([
            Item(PrStatus.ReadyToMerge, number: 1),
            Item(PrStatus.ChecksFailing, newComments: true, number: 2),
            Item(PrStatus.ReadyToMerge, number: 3),
            Item(PrStatus.Draft, number: 4),
        ]);

        // Most pressing first, and the failing PR is named under both of the things it wants.
        Assert.Equal("2 ready to merge · 1 CI failing · 1 with new comments", summary);
    }

    [Fact]
    public void The_strip_is_empty_when_nothing_is_calling_for_anything()
    {
        Assert.Equal(string.Empty, PrGroupViewModel.Summarise([Item(PrStatus.Draft), Item(PrStatus.AwaitingReview, number: 2)]));
        Assert.Equal(string.Empty, PrGroupViewModel.Summarise([]));
    }
}
