using PrStatusTracker.Core;
using PrStatusTracker.Models;
using Xunit;
using static PrStatusTracker.Tests.TestData;

namespace PrStatusTracker.Tests;

public class PrStoreDiffTests
{
    private static Dictionary<string, TrackedPr> Snapshot(params TrackedPr[] items) => items.ToDictionary(t => t.Key);

    [Fact]
    public void Reports_appeared_changed_and_disappeared()
    {
        var previous = Snapshot(Tracked(PrStatus.AwaitingReview, 1), Tracked(PrStatus.YouApproved, 2), Tracked(PrStatus.Draft, 3));
        var next = Snapshot(Tracked(PrStatus.ChangesRequested, 1), Tracked(PrStatus.YouApproved, 2), Tracked(PrStatus.AwaitingYourReview, 4));

        var changes = PrStore.Diff(previous, next);

        Assert.Collection(changes.OrderBy(c => c.Current.Pr.Number),
            c => { Assert.Equal(PrChangeKind.StatusChanged, c.Kind); Assert.Equal(PrStatus.AwaitingReview, c.PreviousStatus); Assert.Equal(PrStatus.ChangesRequested, c.Current.Status); },
            c => { Assert.Equal(PrChangeKind.Disappeared, c.Kind); Assert.Equal(3, c.Current.Pr.Number); },
            c => { Assert.Equal(PrChangeKind.Appeared, c.Kind); Assert.Equal(4, c.Current.Pr.Number); Assert.Null(c.PreviousStatus); });
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Thread_resolution_and_reopening_refresh_without_status_notifications(bool before, bool after)
    {
        var store = TempStore(out var path);
        store.Apply([Tracked(PrStatus.AwaitingReview, author: Me, openThreads: before)]);

        var updates = 0;
        var notifications = 0;
        store.Updated += () => updates++;
        store.Changed += _ => notifications++;

        var changes = store.Apply([Tracked(PrStatus.AwaitingReview, author: Me, openThreads: after)]);

        Assert.Empty(changes);
        Assert.Equal(1, updates);
        Assert.Equal(0, notifications);
        Assert.Equal(after, Assert.Single(store.Current).Pr.HasOpenThreads);
        var reopened = new PrStore(new JsonFileStore<List<TrackedPr>>(path));
        Assert.Equal(after, Assert.Single(reopened.Current).Pr.HasOpenThreads);
        File.Delete(path);
    }

    [Fact]
    public void A_poll_that_changes_nothing_at_all_is_still_silent()
    {
        var store = TempStore(out _);
        store.Apply([Tracked(PrStatus.AwaitingReview, author: Me, openThreads: true)]);

        var updates = 0;
        store.Updated += () => updates++;
        store.Apply([Tracked(PrStatus.AwaitingReview, author: Me, openThreads: true)]);

        Assert.Equal(0, updates);
    }

    /// <summary>
    /// The snapshot restored from disk reaches subscribers only when Start says so. Without it each consumer
    /// has to prime itself, which makes correctness depend on the order the subscriptions were attached.
    /// </summary>
    [Fact]
    public void Start_announces_the_restored_snapshot_to_whoever_subscribed()
    {
        var path = TempPath();
        new PrStore(new JsonFileStore<List<TrackedPr>>(path)).Apply([Tracked(PrStatus.AwaitingReview)]);

        var reopened = new PrStore(new JsonFileStore<List<TrackedPr>>(path));
        var updates = 0;
        reopened.Updated += () => updates++;

        Assert.Equal(0, updates); // the constructor restored it, but silently
        reopened.Start();
        Assert.Equal(1, updates);
        Assert.Single(reopened.Current);
    }

    /// <summary>
    /// With no snapshot on disk there is nothing to announce, and announcing anyway would be destructive:
    /// subscribers prune against what Current holds, and StarStore reads an empty snapshot as "every starred
    /// PR is gone". A missing or corrupt snapshot.json must not cost the user their stars.
    /// </summary>
    [Fact]
    public void Start_is_silent_when_there_was_nothing_to_restore()
    {
        var store = TempStore(out _); // nothing was ever written to this path
        var updates = 0;
        store.Updated += () => updates++;

        store.Start();

        Assert.Equal(0, updates);
    }

    [Fact]
    public void Unchanged_snapshot_yields_no_changes()
    {
        var snapshot = Snapshot(Tracked(PrStatus.AwaitingReview));
        Assert.Empty(PrStore.Diff(snapshot, Snapshot(Tracked(PrStatus.AwaitingReview))));
    }

    [Fact]
    public void First_snapshot_without_persisted_state_is_silent_and_later_ones_are_not()
    {
        var store = TempStore(out var path);

        Assert.Empty(store.Apply([Tracked(PrStatus.AwaitingYourReview)]));
        var changes = store.Apply([Tracked(PrStatus.YouApproved)]);

        Assert.Single(changes);
        Assert.Equal(PrChangeKind.StatusChanged, changes[0].Kind);

        // A second store on the same file starts seeded, so its first Apply is compared against disk.
        var reopened = new PrStore(new JsonFileStore<List<TrackedPr>>(path));
        Assert.Single(reopened.Current);
        Assert.Single(reopened.Apply([]), c => c.Kind == PrChangeKind.Disappeared);

        File.Delete(path);
    }

    [Fact]
    public void Persisted_empty_snapshot_is_still_a_baseline()
    {
        TempStore(out var path).Apply([]); // user had no open PRs; [] is written

        var reopened = new PrStore(new JsonFileStore<List<TrackedPr>>(path));
        var changes = reopened.Apply([Tracked(PrStatus.AwaitingYourReview)]);

        Assert.Single(changes, c => c.Kind == PrChangeKind.Appeared);
        File.Delete(path);
    }

    [Fact]
    public void Snapshot_from_an_older_build_without_newer_fields_still_loads()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Shape written before Labels / Additions / Deletions existed.
        File.WriteAllText(path, """
            [{"Pr":{"Repo":"org/repo","Number":1,"Title":"Old","Url":"https://github.com/org/repo/pull/1","IsDraft":false,
              "Author":"alice","CreatedAt":"2026-09-01T00:00:00+00:00","UpdatedAt":"2026-09-02T00:00:00+00:00",
              "ReviewDecision":null,"Mergeable":"Mergeable","RequestedReviewers":["owen"],"LatestReviews":[],
              "HeadCommittedAt":null,"Checks":"Success","LastCommentAt":"2026-09-02T00:00:00+00:00",
              "LastCommentBy":"bob"},"Status":"AwaitingYourReview"}]
            """);

        var store = new PrStore(new JsonFileStore<List<TrackedPr>>(path));

        var pr = Assert.Single(store.Current).Pr;
        Assert.NotNull(pr.Labels);
        Assert.Empty(pr.Labels);
        Assert.Equal(["owen"], pr.RequestedReviewers);
        Assert.Equal(0, pr.Additions);
        Assert.False(pr.HasOpenThreads);
        File.Delete(path);
    }

    [Fact]
    public void Identical_snapshot_does_not_raise_updated()
    {
        var store = TempStore(out var path);
        var updates = 0;
        store.Updated += () => updates++;

        store.Apply([Tracked(PrStatus.AwaitingYourReview)]);
        store.Apply([Tracked(PrStatus.AwaitingYourReview)]);

        Assert.Equal(1, updates);
        File.Delete(path);
    }
}
