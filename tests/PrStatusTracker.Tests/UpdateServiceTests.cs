using PrStatusTracker.Core;
using Xunit;

namespace PrStatusTracker.Tests;

public class UpdateServiceTests
{
    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("v2.0.0-beta.1", "2.0.0")]
    [InlineData("v1.2", "1.2.0")]
    public void A_tag_parses_to_three_parts(string tag, string expected) =>
        Assert.Equal(Version.Parse(expected), UpdateService.ParseTag(tag));

    [Theory]
    [InlineData("")]
    [InlineData("v")]
    [InlineData("latest")]
    public void A_tag_that_is_not_a_version_offers_no_update(string tag)
    {
        Assert.Null(UpdateService.ParseTag(tag));
        Assert.False(UpdateService.IsNewer(tag, new Version(1, 2, 3)));
    }

    [Theory]
    [InlineData("v1.2.4", true)]
    [InlineData("v2.0.0", true)]
    [InlineData("v1.2.3", false)]
    [InlineData("v1.2.2", false)]
    [InlineData("v1.2.4-rc.1", true)] // the suffix is dropped, so a release candidate of a higher version still counts
    public void Only_a_higher_version_is_newer(string tag, bool expected) =>
        Assert.Equal(expected, UpdateService.IsNewer(tag, new Version(1, 2, 3)));

    /// <summary>The assembly version has four parts, and 1.2.3.0 must not read as newer than the v1.2.3 it was built from.</summary>
    [Fact]
    public void The_running_build_is_not_newer_than_its_own_tag() =>
        Assert.False(UpdateService.IsNewer("v1.2.3", new Version(1, 2, 3, 0)));

    /// <summary>A build from source keeps the csproj's 0.0.0, and no release is an upgrade to something that was never released.</summary>
    [Theory]
    [InlineData("v1.0.1")]
    [InlineData("v0.0.1")]
    public void A_build_from_source_is_offered_nothing(string tag) =>
        Assert.False(UpdateService.IsNewer(tag, new Version(0, 0, 0)));
}
