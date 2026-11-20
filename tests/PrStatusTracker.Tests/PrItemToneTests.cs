using PrStatusTracker.Core;
using PrStatusTracker.Models;
using PrStatusTracker.ViewModels;
using Xunit;
using static PrStatusTracker.Tests.TestData;

namespace PrStatusTracker.Tests;

public class PrItemToneTests
{
    private static readonly Review s_human = new("bob", ReviewState.Approved, T0);
    private static readonly Review s_rabbit = new("coderabbitai", ReviewState.Approved, T0);

    private static PrItemViewModel Item(PrStatus status, IReadOnlyList<Review>? reviews = null, string baseBranch = "main",
        bool openThreads = false, IReadOnlyList<Review>? standing = null, IReadOnlyList<string>? requested = null) =>
        new(new TrackedPr(Pr(author: Me, reviews: reviews, requested: requested, openThreads: openThreads)
            with
        { BaseBranch = baseBranch, DefaultBranch = "main", StandingReviews = standing ?? [] }, status), isStarred: false);

    private static readonly Review s_changes = new("carol", ReviewState.ChangesRequested, T0);

    [Fact]
    public void Ready_and_human_approved_into_the_default_branch_is_good() =>
        Assert.Equal(RowTone.Good, Item(PrStatus.ReadyToMerge, [s_human]).Tone);

    [Fact]
    public void Ready_with_only_a_rabbit_approval_stays_neutral() =>
        Assert.Equal(RowTone.Neutral, Item(PrStatus.ReadyToMerge, [s_rabbit]).Tone);

    [Fact]
    public void Ready_into_another_branch_stays_neutral() =>
        Assert.Equal(RowTone.Neutral, Item(PrStatus.ReadyToMerge, [s_human], baseBranch: "release/2.0").Tone);

    [Fact]
    public void Ready_with_an_unknown_target_stays_neutral() =>
        Assert.Equal(RowTone.Neutral, Item(PrStatus.ReadyToMerge, [s_human], baseBranch: "").Tone);

    [Fact]
    public void Changes_requested_needs_attention() =>
        Assert.Equal(RowTone.Attention, Item(PrStatus.ChangesRequested, [s_changes]).Tone);

    [Fact]
    public void A_rabbit_change_request_needs_attention() =>
        Assert.Equal(RowTone.Attention,
            Item(PrStatus.ChangesRequested, [new("coderabbitai", ReviewState.ChangesRequested, T0)]).Tone);

    [Fact]
    public void Changes_requested_from_a_reviewer_asked_again_waits_on_them()
    {
        // GitHub moves a re-requested reviewer out of latestReviews; the request stands only in StandingReviews.
        var item = Item(PrStatus.ChangesRequested, standing: [s_changes], requested: ["carol"]);
        Assert.Equal(RowTone.Neutral, item.Tone);
        Assert.False(item.HasActionableChangeRequest);
    }

    [Fact]
    public void A_standing_request_from_a_reviewer_not_asked_again_needs_attention() =>
        Assert.Equal(RowTone.Attention, Item(PrStatus.ChangesRequested, standing: [s_changes], requested: ["dana"]).Tone);

    [Fact]
    public void A_newer_approval_clears_the_request() =>
        Assert.Equal(RowTone.Neutral,
            Item(PrStatus.AwaitingReview, [new("carol", ReviewState.Approved, T0.AddMinutes(5))], standing: [s_changes]).Tone);

    [Fact]
    public void Open_threads_need_attention_even_on_a_ready_pr() =>
        Assert.Equal(RowTone.Attention, Item(PrStatus.ReadyToMerge, [s_human], openThreads: true).Tone);

    [Theory]
    [InlineData(PrStatus.AwaitingReview)]
    [InlineData(PrStatus.ApprovedChecksPending)]
    [InlineData(PrStatus.Draft)]
    public void Other_states_stay_neutral(PrStatus status) =>
        Assert.Equal(RowTone.Neutral, Item(status, [s_human]).Tone);
}
