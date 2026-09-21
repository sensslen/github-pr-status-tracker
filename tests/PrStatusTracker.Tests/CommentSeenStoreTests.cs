using PrStatusTracker.Core;
using Xunit;
using static PrStatusTracker.Tests.TestData;

namespace PrStatusTracker.Tests;

public class CommentSeenStoreTests : IDisposable
{
    private static readonly string s_key = Pr(number: 1).Key;
    private static readonly string s_otherKey = Pr(number: 2).Key;

    private readonly string _path = TempPath();

    private CommentSeenStore Open() => new(new JsonFileStore<Dictionary<string, DateTimeOffset>>(_path));

    public void Dispose()
    {
        try { File.Delete(_path); } catch { /* best effort */ }
    }

    [Fact]
    public void A_pr_with_no_comments_is_never_unread() => Assert.False(Open().HasUnread(s_key, null));

    /// <summary>The rule lives here rather than in Sync, so asking before the first poll cannot see a backlog.</summary>
    [Fact]
    public void A_pr_the_store_has_never_heard_of_is_read() => Assert.False(Open().HasUnread(s_key, T0));

    [Fact]
    public void A_comment_on_a_pr_we_have_already_seen_is_unread()
    {
        var seen = Open();
        seen.Sync([(s_key, T0)]);

        Assert.False(seen.HasUnread(s_key, T0));
        Assert.True(seen.HasUnread(s_key, T0.AddMinutes(1)));
    }

    [Fact]
    public void A_pr_seen_for_the_first_time_starts_read()
    {
        var seen = Open();
        seen.Sync([(s_key, T0)]);

        // Seeding is the point: a fresh install must not announce every comment ever left on your open PRs.
        Assert.False(seen.HasUnread(s_key, T0));
    }

    [Fact]
    public void A_pr_first_seen_with_no_comments_is_unread_as_soon_as_one_lands()
    {
        var seen = Open();
        seen.Sync([(s_key, null)]);

        Assert.True(seen.HasUnread(s_key, T0));
    }

    [Fact]
    public void Marking_read_clears_it_and_survives_a_restart()
    {
        var seen = Open();
        seen.Sync([(s_key, null)]);
        seen.MarkRead(s_key, T0);

        Assert.False(seen.HasUnread(s_key, T0));
        Assert.False(Open().HasUnread(s_key, T0));
    }

    [Fact]
    public void Marking_read_does_not_bury_a_comment_that_arrived_since()
    {
        var seen = Open();
        seen.Sync([(s_key, null)]);
        seen.MarkRead(s_key, T0);

        Assert.True(seen.HasUnread(s_key, T0.AddMinutes(1)));
    }

    [Fact]
    public void Marking_read_raises_changed_only_when_something_moved()
    {
        var seen = Open();
        seen.Sync([(s_key, null)]);
        var raised = 0;
        seen.Changed += () => raised++;

        seen.MarkRead(s_key, T0);
        seen.MarkRead(s_key, T0);      // already read this far
        seen.MarkRead(s_key, null);    // nothing to read

        Assert.Equal(1, raised);
    }

    [Fact]
    public void Sync_forgets_prs_that_are_gone()
    {
        var seen = Open();
        seen.Sync([(s_key, T0), (s_otherKey, T0)]);

        seen.Sync([(s_otherKey, T0)]);

        // Forgotten, so it is quiet again until a later Sync seeds it afresh — never judged against a stale mark.
        Assert.False(seen.HasUnread(s_key, T0.AddMinutes(-1)));
        Assert.False(seen.HasUnread(s_otherKey, T0));
    }

    /// <summary>
    /// The trap this store has to avoid: if a PR's first Sync is the one that carries new comment activity,
    /// seeding would record the new comment as the starting point and swallow it. Seeding off the snapshot
    /// restored at startup is what keeps that first Sync ahead of any comment worth flagging.
    /// </summary>
    [Fact]
    public void Seeding_late_would_swallow_the_comment_it_should_have_flagged()
    {
        var seen = Open();
        seen.Sync([(s_key, T0)]);              // at startup, off the restored snapshot
        seen.Sync([(s_key, T0.AddMinutes(5))]); // then a poll brings a comment

        Assert.True(seen.HasUnread(s_key, T0.AddMinutes(5)));
    }

    [Fact]
    public void Sync_is_silent()
    {
        var seen = Open();
        var raised = 0;
        seen.Changed += () => raised++;

        seen.Sync([(s_key, T0)]);
        seen.Sync([]);

        Assert.Equal(0, raised);
    }
}
