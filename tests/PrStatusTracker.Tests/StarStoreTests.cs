using PrStatusTracker.Core;
using Xunit;
using static PrStatusTracker.Tests.TestData;

namespace PrStatusTracker.Tests;

public class StarStoreTests : IDisposable
{
    private static readonly string s_key = Pr(number: 1).Key;
    private static readonly string s_otherKey = Pr(number: 2).Key;

    private readonly string _path = TempPath();

    private StarStore Open() => new(new JsonFileStore<List<string>>(_path));

    public void Dispose()
    {
        try { File.Delete(_path); } catch { /* best effort */ }
    }

    [Fact]
    public void Nothing_is_starred_on_first_run() => Assert.False(Open().IsStarred(s_key));

    [Fact]
    public void Toggle_stars_then_unstars()
    {
        var stars = Open();
        Assert.True(stars.Toggle(s_key));
        Assert.True(stars.IsStarred(s_key));
        Assert.False(stars.Toggle(s_key));
        Assert.False(stars.IsStarred(s_key));
    }

    [Fact]
    public void Stars_survive_a_restart()
    {
        Open().Toggle(s_key);
        Assert.True(Open().IsStarred(s_key));
    }

    [Fact]
    public void Toggle_raises_changed()
    {
        var stars = Open();
        var raised = 0;
        stars.Changed += () => raised++;
        stars.Toggle(s_key);
        stars.Toggle(s_key);
        Assert.Equal(2, raised);
    }

    [Fact]
    public void Retain_drops_stars_for_prs_that_are_gone()
    {
        var stars = Open();
        stars.Toggle(s_key);
        stars.Toggle(s_otherKey);

        stars.Retain([s_otherKey]);

        Assert.False(stars.IsStarred(s_key));
        Assert.True(stars.IsStarred(s_otherKey));
        Assert.False(Open().IsStarred(s_key)); // and the pruning was persisted
    }

    [Fact]
    public void Retain_clears_everything_when_the_snapshot_is_empty()
    {
        var stars = Open();
        stars.Toggle(s_key);

        // Only ever reached off a poll that succeeded, so an empty snapshot means every starred PR is gone.
        stars.Retain([]);

        Assert.False(stars.IsStarred(s_key));
    }

    [Fact]
    public void Retain_is_silent()
    {
        var stars = Open();
        stars.Toggle(s_key);
        var raised = 0;
        stars.Changed += () => raised++;

        stars.Retain([s_otherKey]);

        Assert.Equal(0, raised);
    }
}
