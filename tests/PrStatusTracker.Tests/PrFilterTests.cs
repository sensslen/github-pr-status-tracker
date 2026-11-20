using PrStatusTracker.Core;
using Xunit;
using static PrStatusTracker.Tests.TestData;

namespace PrStatusTracker.Tests;

public class PrFilterTests
{
    private static readonly Models.PullRequest s_sample =
        Pr(number: 42, author: "Alice", repo: "acme/widgets", title: "Fix the uploader retry", labels: ["bug", "Backend"]) with { BaseBranch = "release/7.1" };

    [Fact]
    public void Empty_filter_matches_everything()
    {
        Assert.False(PrFilter.None.IsActive);
        Assert.True(PrFilter.None.Matches(s_sample));
    }

    [Theory]
    [InlineData("uploader")]   // title
    [InlineData("WIDGETS")]    // repo, case-insensitive
    [InlineData("alice")]      // author
    [InlineData("backend")]    // label
    [InlineData("#42")]        // number
    [InlineData("RELEASE/7")]  // target branch
    [InlineData("  retry  ")]  // surrounding whitespace ignored
    public void Free_text_matches_title_repo_author_label_number_and_target_branch(string text) =>
        Assert.True(new PrFilter(Text: text).Matches(s_sample));

    [Fact]
    public void Free_text_that_matches_nothing_excludes() =>
        Assert.False(new PrFilter(Text: "zebra").Matches(s_sample));

    [Fact]
    public void Exact_fields_are_case_insensitive_and_combine()
    {
        Assert.True(new PrFilter(Author: "ALICE", Label: "BUG", Repo: "Acme/Widgets").Matches(s_sample));
        Assert.False(new PrFilter(Author: "bob").Matches(s_sample));
        Assert.False(new PrFilter(Label: "docs").Matches(s_sample));
        Assert.False(new PrFilter(Repo: "acme/other").Matches(s_sample));
        Assert.False(new PrFilter(Author: "alice", Label: "docs").Matches(s_sample)); // all set fields must hold
    }

    [Fact]
    public void Whitespace_only_text_is_not_active() => Assert.False(new PrFilter(Text: "   ").IsActive);
}
