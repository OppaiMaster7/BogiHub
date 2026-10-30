using BogiHub.Core.Updates;

namespace BogiHub.Core.Tests;

public class NewerReleaseTests
{
    private static ReleaseEntry R(string tag, bool draft = false) =>
        new(tag, draft, NewerRelease.ReleasesPrefix + "tag/" + tag);

    [Theory]
    [InlineData("v0.1.2", "0.1.2")]
    [InlineData("0.2.0", "0.2.0")]
    [InlineData("V1.10.3", "1.10.3")]
    public void Plain_version_tags_parse(string tag, string expected) =>
        Assert.Equal(Version.Parse(expected), NewerRelease.ParseTag(tag));

    [Theory]
    [InlineData("v0.2.0-beta")]
    [InlineData("nightly")]
    [InlineData("v1.2")]
    [InlineData("v1.2.3.4")]
    public void Odd_tags_are_skipped(string tag) => Assert.Null(NewerRelease.ParseTag(tag));

    [Fact]
    public void Offers_the_highest_newer_release_including_pre_releases()
    {
        var found = NewerRelease.Find([R("v0.1.0"), R("v0.1.3"), R("v0.1.10"), R("v0.1.2")], new Version(0, 1, 1, 0));
        Assert.Equal("v0.1.10", found?.Tag);
    }

    [Fact]
    public void Nothing_when_already_newest()
    {
        Assert.Null(NewerRelease.Find([R("v0.1.0"), R("v0.1.1")], new Version(0, 1, 1, 0)));
        Assert.Null(NewerRelease.Find([], new Version(0, 1, 1)));
    }

    [Fact]
    public void Drafts_and_foreign_pages_are_never_offered()
    {
        var foreign = new ReleaseEntry("v9.0.0", false, "https://example.com/BogiHub/releases/tag/v9.0.0");
        Assert.Null(NewerRelease.Find([R("v0.2.0", draft: true), foreign], new Version(0, 1, 1)));
    }
}
