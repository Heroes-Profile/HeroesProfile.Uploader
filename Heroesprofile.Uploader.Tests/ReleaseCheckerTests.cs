using Heroesprofile.Uploader.Desktop;
using Heroesprofile.Uploader.Desktop.Updates;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Heroesprofile.Uploader.Tests;

/// <summary>The "is there a newer release?" check used by `run`, the Linux tarball and dev builds.</summary>
public class ReleaseCheckerTests
{
    // A trimmed GitHub releases API response, newest first as GitHub returns them.
    private static readonly JArray Releases = JArray.Parse("""
        [
          { "tag_name": "v3.1.0", "draft": true,  "prerelease": false, "html_url": "https://example/v3.1.0" },
          { "tag_name": "v3.0.0-beta.2", "draft": false, "prerelease": true, "html_url": "https://example/v3.0.0-beta.2" },
          { "tag_name": "v2.10.0", "draft": false, "prerelease": false, "html_url": "https://example/v2.10.0" },
          { "tag_name": "v2.9.0", "draft": false, "prerelease": false, "html_url": "https://example/v2.9.0" },
          { "tag_name": "not-a-version", "draft": false, "prerelease": false, "html_url": "https://example/x" }
        ]
        """);

    private static ReleaseVersion V(string v) => ReleaseVersion.Parse(v)!;

    [Fact]
    public void Finds_the_newest_stable_release()
    {
        var release = ReleaseChecker.Newest(Releases, includePreReleases: false, current: V("2.9.0"));

        Assert.Equal("2.10.0", release!.Version.ToString());
        Assert.Equal("https://example/v2.10.0", release.Url);
    }

    [Fact]
    public void Includes_prereleases_only_when_beta_updates_are_on()
    {
        Assert.Equal("3.0.0-beta.2", ReleaseChecker.Newest(Releases, includePreReleases: true, current: V("2.9.0"))!.Version.ToString());
    }

    [Fact]
    public void Never_offers_a_draft()
    {
        Assert.DoesNotContain(new[] { true, false }, pre => ReleaseChecker.Newest(Releases, pre, V("2.9.0"))!.Version.ToString() == "3.1.0");
    }

    [Fact]
    public void Nothing_when_already_on_the_newest()
    {
        Assert.Null(ReleaseChecker.Newest(Releases, includePreReleases: false, current: V("2.10.0")));
    }
}
