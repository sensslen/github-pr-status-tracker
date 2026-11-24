using PrStatusTracker.Models;
using PrStatusTracker.ViewModels;
using Xunit;
using static PrStatusTracker.Tests.TestData;

namespace PrStatusTracker.Tests;

/// <summary>A tab narrows its rows by its own bar, and two tabs only move together if they share one.</summary>
public class PrGroupViewModelTests
{
    private static PrItemViewModel Item(string author, int number, string repo = "org/repo",
        PrStatus status = PrStatus.AwaitingReview, bool starred = false, DateTimeOffset? createdAt = null,
        bool openThreads = false) =>
        new(new(Pr(number: number, author: author, repo: repo, openThreads: openThreads)
            with
        { CreatedAt = createdAt ?? T0.AddDays(-1) }, status), starred);

    private static PrItemViewModel TeamOnly(int number, string repo = "org/repo") =>
        Item(Other, number, repo, PrStatus.RequestedViaTeam);

    [Theory]
    [InlineData(PrBucket.NeedsYou, PrStatus.AwaitingYourReview)]
    [InlineData(PrBucket.WaitingOnOthers, PrStatus.YouApproved)]
    [InlineData(PrBucket.YourPrs, PrStatus.AwaitingReview)]
    public void Every_tab_groups_repositories_alphabetically_with_stars_inside_their_header(PrBucket bucket, PrStatus status)
    {
        var group = new PrGroupViewModel(bucket, new PrFilterViewModel());
        group.Replace([
            Item(Me, 1, "org/Zeta", status, starred: true),
            Item(Me, 2, "org/alpha", status),
            Item(Me, 3, "org/Zeta", status),
            Item(Me, 4, "org/alpha", status, starred: true),
        ]);

        Assert.Equal(["org/alpha", "org/Zeta"], group.Repositories.Select(r => r.Name));
        Assert.Equal([4, 2], group.Repositories[0].Items.Select(i => i.Pr.Number));
        Assert.Equal([1, 3], group.Repositories[1].Items.Select(i => i.Pr.Number));
        Assert.Equal(group.Repositories.SelectMany(r => r.Items), group.Repositories.SelectMany(r => r.Items));
        Assert.Equal(4, group.TotalCount);
        Assert.Equal($"{bucket.Label()} (4)", group.Header);
    }

    [Fact]
    public void Drafts_sink_below_everything_then_stars_lead_each_half()
    {
        var group = new PrGroupViewModel(PrBucket.YourPrs, new PrFilterViewModel());
        group.Replace([
            Item(Me, 1, status: PrStatus.Draft, starred: true),
            Item(Me, 2, starred: true, createdAt: T0),
            Item(Me, 7, status: PrStatus.Draft),
            Item(Me, 6, createdAt: T0),
            Item(Me, 5, status: PrStatus.Draft, openThreads: true),
            Item(Me, 4, status: PrStatus.ChecksFailing),
            Item(Me, 3, status: PrStatus.ReadyToMerge),
        ]);

        Assert.Equal([2, 3, 4, 6, 1, 5, 7], group.Repositories.SelectMany(r => r.Items).Select(i => i.Pr.Number));
        Assert.Equal("1 ready to merge · 1 CI failing · 1 with open threads", group.Attention);
    }

    /// <summary>Threads behind a Draft or In discussion pill do not show in its text, so they do not move it.</summary>
    [Fact]
    public void Rows_with_the_same_pill_sort_by_age_alone()
    {
        var group = new PrGroupViewModel(PrBucket.YourPrs, new PrFilterViewModel());
        group.Replace([
            Item(Me, 4, status: PrStatus.Draft, openThreads: true, createdAt: T0),
            Item(Me, 3, status: PrStatus.Draft, createdAt: T0.AddDays(-1)),
            Item(Me, 2, status: PrStatus.AwaitingReview, openThreads: true, createdAt: T0),
            Item(Me, 1, status: PrStatus.ApprovedChecksPending, openThreads: true, createdAt: T0.AddDays(-1)),
        ]);

        Assert.Equal([1, 2, 3, 4], group.Repositories.SelectMany(r => r.Items).Select(i => i.Pr.Number));
    }

    [Theory]
    [InlineData(PrBucket.NeedsYou, PrStatus.AwaitingYourReview, PrStatus.AwaitingYourRereview)]
    [InlineData(PrBucket.WaitingOnOthers, PrStatus.YouApproved, PrStatus.ReviewerDraft)]
    [InlineData(PrBucket.YourPrs, PrStatus.AwaitingReview, PrStatus.Draft)]
    public void Status_then_age_then_number_break_ties_regardless_of_input_order(
        PrBucket bucket, PrStatus first, PrStatus last)
    {
        var group = new PrGroupViewModel(bucket, new PrFilterViewModel());
        PrItemViewModel[] rows = [
            Item(Me, 1, status: last, createdAt: T0.AddDays(-3)),
            Item(Me, 4, status: first),
            Item(Me, 2, status: first),
            Item(Me, 3, status: first, createdAt: T0.AddDays(-2)),
        ];

        foreach (var input in new[] { rows, rows.Reverse().ToArray() })
        {
            group.Replace(input);
            Assert.Equal([3, 2, 4, 1], group.Repositories.SelectMany(r => r.Items).Select(i => i.Pr.Number));
        }
    }

    [Fact]
    public void Repository_spelling_breaks_case_insensitive_sort_ties()
    {
        var group = new PrGroupViewModel(PrBucket.YourPrs, new PrFilterViewModel());
        PrItemViewModel[] rows = [Item(Me, 1, "org/repo"), Item(Me, 2, "Org/Repo")];

        foreach (var input in new[] { rows, rows.Reverse().ToArray() })
        {
            group.Replace(input);
            Assert.Equal(["Org/Repo", "org/repo"], group.Repositories.Select(r => r.Name));
        }
    }

    [Fact]
    public void Filtering_removes_empty_repository_headers_and_counts_only_visible_callouts()
    {
        var filter = new PrFilterViewModel { Text = "#2" };
        var group = new PrGroupViewModel(PrBucket.YourPrs, filter);
        PrItemViewModel[] rows = [
            Item(Me, 1, "org/hidden", PrStatus.ChecksFailing),
            Item(Me, 2, "org/visible", openThreads: true),
        ];
        group.Replace(rows);

        Assert.Equal("org/visible", Assert.Single(group.Repositories).Name);
        Assert.Equal("Your PRs (1 of 2)", group.Header);
        Assert.Equal("1 with open threads", group.Attention);

        filter.Text = "no match";
        group.Replace(rows);
        Assert.Empty(group.Repositories);
        Assert.Empty(group.Repositories.SelectMany(r => r.Items));
        Assert.True(group.IsEmpty);
        Assert.Equal(string.Empty, group.Attention);

        filter.Clear();
        group.Replace(rows);
        Assert.Equal(2, group.Repositories.Count);
    }

    [Fact]
    public void Hiding_team_only_requests_removes_their_empty_repository_header()
    {
        var group = new PrGroupViewModel(PrBucket.NeedsYou, new PrFilterViewModel()) { HideTeamOnly = true };
        group.Replace([
            TeamOnly(1, "org/hidden"),
            Item(Other, 2, "org/visible", PrStatus.AwaitingYourReview),
        ]);

        Assert.Equal("org/visible", Assert.Single(group.Repositories).Name);
        Assert.Equal("Needs you (1 of 2)", group.Header);
        Assert.Equal("Hide team-only requests (1 hidden)", group.HideTeamOnlyLabel);
    }

    [Theory]
    [InlineData(PrBucket.NeedsYou, PrStatus.AwaitingYourReview)]
    [InlineData(PrBucket.WaitingOnOthers, PrStatus.YouApproved)]
    [InlineData(PrBucket.YourPrs, PrStatus.AwaitingReview)]
    public void Collapse_state_survives_refresh_and_filtering_without_changing_counts(PrBucket bucket, PrStatus status)
    {
        var filter = new PrFilterViewModel();
        var group = new PrGroupViewModel(bucket, filter);
        PrItemViewModel[] rows = [Item(Me, 1, status: status, starred: true, openThreads: true)];
        group.Replace(rows);
        var repository = Assert.Single(group.Repositories);
        Assert.True(repository.IsExpanded);
        var attention = group.Attention;

        repository.IsExpanded = false;
        group.Replace([Item(Me, 2, status: status), .. rows]);
        Assert.Same(repository, Assert.Single(group.Repositories));
        Assert.False(repository.IsExpanded);
        Assert.Equal([1, 2], repository.Items.Select(i => i.Pr.Number));
        Assert.Equal($"{bucket.Label()} (2)", group.Header);
        Assert.Equal(2, group.TotalCount);
        Assert.False(group.IsEmpty);
        Assert.Equal(attention, group.Attention);

        filter.Text = "no match";
        group.Replace(rows);
        Assert.Empty(group.Repositories);
        filter.Clear();
        group.Replace(rows);
        Assert.Same(repository, Assert.Single(group.Repositories));
        Assert.False(repository.IsExpanded);

        var otherTab = new PrGroupViewModel(bucket, filter);
        otherTab.Replace(rows);
        Assert.True(Assert.Single(otherTab.Repositories).IsExpanded);
        repository.IsExpanded = true;
        group.Replace(rows);
        Assert.True(repository.IsExpanded);
    }

    [Fact]
    public void Tabs_sharing_a_bar_narrow_together()
    {
        var shared = new PrFilterViewModel();
        var needsYou = new PrGroupViewModel(PrBucket.NeedsYou, shared);
        var waiting = new PrGroupViewModel(PrBucket.WaitingOnOthers, shared);

        shared.Author = Other;
        needsYou.Replace([Item(Me, 1), Item(Other, 2)]);
        waiting.Replace([Item(Other, 3)]);

        Assert.Equal(2, Assert.Single(needsYou.Repositories.SelectMany(r => r.Items)).Pr.Number);
        Assert.Single(waiting.Repositories.SelectMany(r => r.Items));
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

        Assert.Empty(needsYou.Repositories.SelectMany(r => r.Items));
        Assert.Equal(2, yours.Repositories.SelectMany(r => r.Items).Count());
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

        Assert.Equal(1, Assert.Single(needsYou.Repositories.SelectMany(r => r.Items)).Pr.Number);
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

        Assert.Single(waiting.Repositories.SelectMany(r => r.Items));
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

        Assert.Empty(needsYou.Repositories.SelectMany(r => r.Items));
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

    [Fact]
    public void Restored_collapsed_sections_start_collapsed_and_others_expanded()
    {
        var group = new PrGroupViewModel(PrBucket.YourPrs, new PrFilterViewModel());
        group.RestoreCollapsed(["org/folded", "org/absent"]);
        group.Replace([Item(Me, 1, "org/folded"), Item(Me, 2, "org/open")]);

        Assert.Equal([false, true], group.Repositories.Select(r => r.IsExpanded));
    }

    [Fact]
    public void Toggling_a_section_reports_the_collapsed_set_including_absent_repositories()
    {
        var group = new PrGroupViewModel(PrBucket.YourPrs, new PrFilterViewModel());
        group.RestoreCollapsed(["org/absent"]);
        group.Replace([Item(Me, 1, "org/a"), Item(Me, 2, "org/b")]);
        var raised = 0;
        group.CollapseChanged += () => raised++;

        group.Repositories[0].IsExpanded = false;
        Assert.Equal(1, raised);
        Assert.Equal(["org/a", "org/absent"], group.Collapsed.Order(StringComparer.Ordinal));

        group.Repositories[0].IsExpanded = true;
        Assert.Equal(2, raised);
        Assert.Equal(["org/absent"], group.Collapsed);
    }

    [Fact]
    public void A_refresh_does_not_report_a_collapse_change()
    {
        var group = new PrGroupViewModel(PrBucket.YourPrs, new PrFilterViewModel());
        group.RestoreCollapsed(["org/a"]);
        var raised = 0;
        group.CollapseChanged += () => raised++;

        group.Replace([Item(Me, 1, "org/a")]);
        group.Replace([Item(Me, 1, "org/a"), Item(Me, 2, "org/a")]);

        Assert.Equal(0, raised);
    }

    [Fact]
    public void Collapsed_repositories_survive_a_settings_round_trip()
    {
        var path = TempPath();
        var file = new Core.JsonFileStore<AppSettings>(path);
        file.Save(new AppSettings { CollapsedRepositories = { ["YourPrs"] = ["org/a", "org/b"] } });

        var loaded = new Core.JsonFileStore<AppSettings>(path).Load(() => new AppSettings());
        Assert.Equal(["org/a", "org/b"], loaded.CollapsedRepositories["YourPrs"]);
    }

    [Fact]
    public void A_settings_file_without_collapsed_repositories_loads_empty()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{ "PollIntervalMinutes": 3 }""");

        var loaded = new Core.JsonFileStore<AppSettings>(path).Load(() => new AppSettings());
        Assert.Empty(loaded.CollapsedRepositories);
    }
}
