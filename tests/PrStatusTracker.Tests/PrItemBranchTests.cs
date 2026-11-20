using PrStatusTracker.Core;
using PrStatusTracker.Models;
using PrStatusTracker.ViewModels;
using Xunit;
using static PrStatusTracker.Tests.TestData;

namespace PrStatusTracker.Tests;

public class PrItemBranchTests
{
    private static PrItemViewModel Item(string baseBranch, string headBranch = "alice/fix", string defaultBranch = "main") =>
        new(new TrackedPr(Pr() with { BaseBranch = baseBranch, HeadBranch = headBranch, DefaultBranch = defaultBranch },
            PrStatus.AwaitingYourReview), isStarred: false);

    [Fact]
    public void A_pr_into_the_default_branch_names_it_without_a_warning()
    {
        var item = Item("main");
        Assert.True(item.HasTargetBranch);
        Assert.Equal("main", item.TargetBranch);
        Assert.False(item.TargetsNonDefaultBranch);
        Assert.Equal("Merges alice/fix into main", item.BranchTip);
    }

    [Fact]
    public void A_pr_into_another_branch_is_flagged_and_names_the_default()
    {
        var item = Item("release/2.0");
        Assert.True(item.TargetsNonDefaultBranch);
        Assert.Equal("Merges alice/fix into release/2.0, not the default branch main", item.BranchTip);
    }

    [Fact]
    public void An_unknown_default_branch_never_flags()
    {
        Assert.False(Item("release/2.0", defaultBranch: "").TargetsNonDefaultBranch);
    }

    [Fact]
    public void A_snapshot_from_before_branches_were_fetched_shows_no_branch()
    {
        var item = Item("", headBranch: "", defaultBranch: "");
        Assert.False(item.HasTargetBranch);
        Assert.False(item.TargetsNonDefaultBranch);
    }

    [Fact]
    public void Fetching_branches_for_the_first_time_is_a_change_of_state()
    {
        var before = Pr();
        Assert.False(before.SameStateAs(before with { BaseBranch = "main" }));
    }
}
