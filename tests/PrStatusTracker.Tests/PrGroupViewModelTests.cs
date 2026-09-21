using PrStatusTracker.Models;
using PrStatusTracker.ViewModels;
using Xunit;
using static PrStatusTracker.Tests.TestData;

namespace PrStatusTracker.Tests;

/// <summary>A tab narrows its rows by its own bar, and two tabs only move together if they share one.</summary>
public class PrGroupViewModelTests
{
    private static PrItemViewModel Item(string author, int number, string repo = "org/repo") =>
        new(new(Pr(number: number, author: author, repo: repo), PrStatus.AwaitingReview), isStarred: false, hasNewComments: false);

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
