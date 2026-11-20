using Avalonia.Threading;
using PrStatusTracker.Core;
using PrStatusTracker.GitHub;
using PrStatusTracker.Models;
using PrStatusTracker.Notifications;
using PrStatusTracker.Platform;
using PrStatusTracker.ViewModels;
using Xunit;
using static PrStatusTracker.Tests.TestData;

namespace PrStatusTracker.Tests;

public class PrItemCalloutTests
{
    private static PrItemViewModel Item(PrStatus status, bool openThreads = false, int number = 1,
        bool unresolvedThreads = false) =>
        new(Tracked(status, number: number, author: Me, openThreads: openThreads, unresolvedThreads: unresolvedThreads),
            isStarred: false);

    [Fact]
    public void A_quiet_pr_calls_for_nothing()
    {
        var item = Item(PrStatus.AwaitingReview);
        Assert.False(item.NeedsAttention);
        Assert.Empty(item.Callouts);
        Assert.Equal(PrCallout.None, item.Callout);
        Assert.Equal("Awaiting review", item.StatusLabel);
        Assert.Equal(string.Empty, item.OpenThreadsTip);
    }

    [Fact]
    public void Open_threads_alone_agree_on_the_pill_tooltip_and_summary()
    {
        var item = Item(PrStatus.AwaitingReview, openThreads: true);
        Assert.Equal([PrCallout.OpenThreads], item.Callouts);
        Assert.True(item.NeedsAttention);
        Assert.Equal("Open threads", item.StatusLabel);
        Assert.Equal("Unresolved review threads awaiting your reply", item.OpenThreadsTip);
        Assert.Equal("1 with open threads", PrGroupViewModel.Summarise([item]));
    }

    [Theory]
    [InlineData(PrStatus.ChecksFailing)]
    [InlineData(PrStatus.MergeConflict)]
    [InlineData(PrStatus.ReadyToMergeConflict)]
    [InlineData(PrStatus.ChangesRequested)]
    [InlineData(PrStatus.NoReviewers)]
    public void A_status_that_wants_something_keeps_its_own_pill(PrStatus status)
    {
        var item = Item(status, openThreads: true);
        Assert.Equal([status.Callout(), PrCallout.OpenThreads], item.Callouts);
        Assert.Equal(status.Label(), item.StatusLabel);
        Assert.True(item.HasOpenThreads);
    }

    /// <summary>A branch that requires conversation resolution will not merge with a thread open, whoever it
    /// waits on, so an otherwise ready PR is an open-threads PR.</summary>
    [Theory]
    [InlineData(true, false, "Unresolved review threads awaiting your reply")]
    [InlineData(false, true, "Approved, but unresolved review threads block the merge")]
    public void Open_threads_take_the_place_of_ready_to_merge(bool openThreads, bool unresolvedThreads, string tip)
    {
        var item = Item(PrStatus.ReadyToMerge, openThreads: openThreads, unresolvedThreads: unresolvedThreads || openThreads);
        Assert.Equal([PrCallout.OpenThreads], item.Callouts);
        Assert.Equal("Open threads", item.StatusLabel);
        Assert.Equal(tip, item.OpenThreadsTip);
        Assert.Equal(RowTone.Attention, item.Tone);
    }

    [Theory]
    [InlineData(PrStatus.AwaitingReview)]
    [InlineData(PrStatus.ApprovedChecksPending)]
    [InlineData(PrStatus.ChecksFailing)]
    public void Threads_waiting_on_a_reviewer_stay_quiet_on_a_pr_that_is_not_ready(PrStatus status)
    {
        var item = Item(status, unresolvedThreads: true);
        Assert.DoesNotContain(PrCallout.OpenThreads, item.Callouts);
        Assert.Equal(status.Label(), item.StatusLabel);
    }

    [Fact]
    public void The_strip_counts_every_callout_a_row_carries()
    {
        var summary = PrGroupViewModel.Summarise([
            Item(PrStatus.ReadyToMerge, openThreads: true, number: 1),
            Item(PrStatus.ChecksFailing, openThreads: true, number: 2),
            Item(PrStatus.ReadyToMerge, number: 3),
            Item(PrStatus.Draft, number: 4),
        ]);

        Assert.Equal("1 ready to merge · 1 CI failing · 2 with open threads", summary);
    }

    [Fact]
    public void The_strip_is_empty_when_nothing_is_calling_for_anything()
    {
        Assert.Equal(string.Empty, PrGroupViewModel.Summarise([Item(PrStatus.Draft), Item(PrStatus.AwaitingReview, number: 2)]));
        Assert.Equal(string.Empty, PrGroupViewModel.Summarise([]));
    }

    [Fact]
    public void Current_thread_work_survives_opening_and_updates_the_summary_and_tray()
    {
        var paths = Enumerable.Range(0, 4).Select(_ => TempPath()).ToArray();
        try
        {
            var store = new PrStore(new JsonFileStore<List<TrackedPr>>(paths[0]));
            var stars = new StarStore(new JsonFileStore<List<string>>(paths[1]));
            var settings = new AppSettings();
            using var poller = new PollingService(new GitHubClient(new GhCommandRunner()), store, () => settings);
            using var notifications = new NotificationService(store, new NullNotifier(),
                new JsonFileStore<List<NotificationRecord>>(paths[2]), () => settings);
            var tracked = new TrackedPr(Pr(author: Me, openThreads: true) with { Url = "" }, PrStatus.ReadyToMerge);
            store.Apply([tracked]);
            var vm = new MainViewModel(store, stars, poller, notifications, settings,
                new JsonFileStore<AppSettings>(paths[3]), new UnsupportedAutoStart(), new GhCommandRunner(),
                new UpdateService(new GhCommandRunner(), () => { }));

            void AssertWork(bool open)
            {
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(open, Assert.Single(vm.YourPrs.Repositories.SelectMany(r => r.Items)).HasOpenThreads);
                Assert.Equal(open ? "1 with open threads" : "1 ready to merge", vm.YourPrs.Attention);
                Assert.Equal(open ? "0 need you · 0 waiting · 1 yours (1 need attention)"
                    : "0 need you · 0 waiting · 1 yours (1 ready)", vm.Summary);
                Assert.Equal(open ? TrayState.NeedsYou : TrayState.Ready, vm.TrayState);
                Assert.Empty(notifications.History);
            }

            AssertWork(true);
            MainViewModel.OpenPr(Assert.Single(vm.YourPrs.Repositories.SelectMany(r => r.Items)));
            stars.Toggle(tracked.Key);
            AssertWork(true);

            foreach (var open in new[] { false, true })
            {
                tracked = tracked with { Pr = tracked.Pr with { HasOpenThreads = open } };
                store.Apply([tracked]);
                AssertWork(open);
                tracked = tracked with { Pr = tracked.Pr with { Title = "Edited title", UpdatedAt = T0.AddHours(1) } };
                store.Apply([tracked]);
                AssertWork(open);
            }
        }
        finally
        {
            foreach (var path in paths)
            {
                File.Delete(path);
            }
        }

    }

    [Fact]
    public async Task A_failed_refresh_preserves_thread_work_and_reports_the_error()
    {
        var store = TempStore(out var path);
        try
        {
            store.Apply([Tracked(PrStatus.AwaitingReview, author: Me, openThreads: true)]);
            var github = new GitHubClient((args, ct) => throw new GhException(GhFailureKind.Network, "offline"));
            using var poller = new PollingService(github, store, () => new AppSettings());

            await poller.RefreshNowAsync();

            Assert.True(Assert.Single(store.Current).Pr.HasOpenThreads);
            Assert.Null(poller.LastSuccess);
            Assert.Contains("Could not reach GitHub", Assert.IsType<GhException>(poller.LastError).UserMessage);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
