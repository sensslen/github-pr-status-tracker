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
              "HeadCommittedAt":null,"Checks":"Success"},"Status":"AwaitingYourReview"}]
            """);

        var store = new PrStore(new JsonFileStore<List<TrackedPr>>(path));

        var pr = Assert.Single(store.Current).Pr;
        Assert.NotNull(pr.Labels);
        Assert.Empty(pr.Labels);
        Assert.Equal(["owen"], pr.RequestedReviewers);
        Assert.Equal(0, pr.Additions);
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
