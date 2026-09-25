using PrStatusTracker.Models;
using PrStatusTracker.ViewModels;
using Xunit;
using static PrStatusTracker.Tests.TestData;

namespace PrStatusTracker.Tests;

/// <summary>A tab narrows its rows by its own bar, and two tabs only move together if they share one.</summary>
public class PrGroupViewModelTests
{
    private static PrItemViewModel Item(string author, int number, string repo = "org/repo",
        PrStatus status = PrStatus.AwaitingReview) =>
        new(new(Pr(number: number, author: author, repo: repo), status), isStarred: false);

    private static PrItemViewModel TeamOnly(int number, string repo = "org/repo") =>
        Item(Other, number, repo, PrStatus.RequestedViaTeam);

    [Fact]
    public void Tabs_sharing_a_bar_narrow_together()
    {
        var shared = new PrFilterViewModel();
        var needsYou = new PrGroupViewModel(PrBucket.NeedsYou, shared);
        var waiting = new PrGroupViewModel(PrBucket.WaitingOnOthers, shared);

        shared.Author = Other;
        needsYou.Replace([Item(Me, 1), Item(Other, 2)]);
        waiting.Replace([Item(Other, 3)]);

        Assert.Equal(2, Assert.Single(needsYou.Items).Pr.Number);
        Assert.Single(waiting.Items);
        Assert.Equal("Needs you (1 of 2)", needsYou.Header);
    }

    [Fact]
    public void A_tab_with_its_own_bar_is_untouched_by_the_other_one()
    {
        var shared = new PrFilterViewModel();
        var mine = new PrFilterViewModel(withAuthor: false);
        var needsYou = new PrGroupViewModel(PrBucket.NeedsYou, shared);
        var yours = new PrGroupViewModel(PrBucket.YourPrs, mine);

        // Narrowing the review queue to one repo must not narrow your own PRs as well.
        shared.Repo = "org/other";
        needsYou.Replace([Item(Other, 1)]);
        yours.Replace([Item(Me, 2), Item(Me, 3)]);

        Assert.Empty(needsYou.Items);
        Assert.Equal(2, yours.Items.Count);
        Assert.Equal("Your PRs (2)", yours.Header);
    }

    [Fact]
    public void A_bar_without_an_author_dropdown_has_no_author_dimension_to_leave_behind()
    {
        var mine = new PrFilterViewModel(withAuthor: false);

        // Nothing can set this from the UI, and Current drops it even if something does.
        mine.Author = Other;

        Assert.Null(mine.Current.Author);
        Assert.False(mine.Current.IsActive);
    }

    [Fact]
    public void Each_bar_offers_only_the_choices_in_the_rows_it_filters()
    {
        var mine = new PrFilterViewModel(withAuthor: false);

        mine.RebuildOptions([Item(Me, 1, repo: "org/mine")]);

        Assert.Equal([PrFilterViewModel.AnyRepo, "org/mine"], mine.Repos);
    }

    [Fact]
    public void Needs_you_can_drop_the_prs_only_your_team_was_asked_for()
    {
        var needsYou = new PrGroupViewModel(PrBucket.NeedsYou, new PrFilterViewModel()) { HideTeamOnly = true };

        needsYou.Replace([Item(Other, 1, status: PrStatus.AwaitingYourReview), TeamOnly(2)]);

        Assert.Equal(1, Assert.Single(needsYou.Items).Pr.Number);
        // View-only: the tray and the summary line read TotalCount, and it still counts what is hidden.
        Assert.Equal(2, needsYou.TotalCount);
        // With no bar active, the header and the checkbox are the only things accounting for the missing row.
        Assert.Equal("Needs you (1 of 2)", needsYou.Header);
        Assert.Equal("Hide team-only requests (1 hidden)", needsYou.HideTeamOnlyLabel);
    }

    [Fact]
    public void The_toggle_is_offered_by_the_tab_those_rows_land_in_and_no_other()
    {
        // Pinned to the partition rather than to the name of a tab: re-file RequestedViaTeam and this follows it.
        foreach (var bucket in Enum.GetValues<PrBucket>())
        {
            Assert.Equal(PrStatus.RequestedViaTeam.Bucket() == bucket,
                new PrGroupViewModel(bucket, new PrFilterViewModel()).CanHideTeamOnly);
        }
    }

    [Fact]
    public void A_tab_that_does_not_offer_the_toggle_cannot_be_narrowed_by_it()
    {
        // Waiting on others shares Needs you's bar, so a flag set on it must not be able to hide anything.
        var waiting = new PrGroupViewModel(PrBucket.WaitingOnOthers, new PrFilterViewModel()) { HideTeamOnly = true };

        waiting.Replace([TeamOnly(1)]);

        Assert.Single(waiting.Items);
        Assert.Equal("Waiting on others (1)", waiting.Header);
        Assert.Equal("Hide team-only requests", waiting.HideTeamOnlyLabel);
    }

    [Fact]
    public void The_hidden_count_names_only_what_unchecking_the_box_would_bring_back()
    {
        var bar = new PrFilterViewModel();
        var needsYou = new PrGroupViewModel(PrBucket.NeedsYou, bar) { HideTeamOnly = true };

        // The bar already excludes the team-only PR in the other repo; the toggle cannot claim that one too.
        bar.Repo = "org/repo";
        needsYou.Replace([TeamOnly(1), TeamOnly(2, "org/other")]);

        Assert.Empty(needsYou.Items);
        Assert.Equal("Hide team-only requests (1 hidden)", needsYou.HideTeamOnlyLabel);
    }

    [Fact]
    public void A_tab_narrowed_to_nothing_does_not_claim_the_bucket_is_empty()
    {
        var bar = new PrFilterViewModel();
        var needsYou = new PrGroupViewModel(PrBucket.NeedsYou, bar);

        // Truly empty: the bucket's own wording is the honest one.
        needsYou.Replace([]);
        Assert.True(needsYou.IsEmpty);
        Assert.Equal(PrBucket.NeedsYou.EmptyText(), needsYou.EmptyText);

        // Narrowed to nothing by the bar, and then by the toggle: rows exist, so the bucket's claim would be false.
        bar.Text = "nothing matches this";
        needsYou.Replace([Item(Other, 1, status: PrStatus.AwaitingYourReview)]);
        Assert.True(needsYou.IsEmpty);
        Assert.Equal("No pull requests match.", needsYou.EmptyText);

        bar.Clear();
        needsYou.HideTeamOnly = true;
        needsYou.Replace([TeamOnly(1)]);
        Assert.True(needsYou.IsEmpty);
        Assert.Equal("Needs you (0 of 1)", needsYou.Header);
        Assert.Equal("No pull requests match.", needsYou.EmptyText);

        // And back, once the narrowing goes.
        needsYou.HideTeamOnly = false;
        needsYou.Replace([TeamOnly(1)]);
        Assert.False(needsYou.IsEmpty);
        Assert.Equal(PrBucket.NeedsYou.EmptyText(), needsYou.EmptyText);
    }

    [Fact]
    public void Clear_resets_the_bar_and_reports_it()
    {
        var filter = new PrFilterViewModel();
        var changes = 0;
        filter.Changed += () => changes++;

        filter.Text = "widget";
        Assert.True(filter.IsActive);

        filter.Clear();

        Assert.False(filter.IsActive);
        Assert.False(filter.Current.IsActive);
        Assert.Equal(2, changes); // the edit, then the clear
    }
}
