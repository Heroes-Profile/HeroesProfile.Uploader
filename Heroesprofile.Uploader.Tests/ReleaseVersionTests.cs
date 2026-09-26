using Heroesprofile.Uploader.Desktop;
using Xunit;

namespace Heroesprofile.Uploader.Tests;

public class ReleaseVersionTests
{
    [Theory]
    [InlineData("v2.8.0", "2.8.0")]
    [InlineData("2.8.0", "2.8.0")]
    [InlineData("linux-v2.8.0-test.2", "2.8.0-test.2")]
    [InlineData("heroesprofile-uploader (Linux) 2.8.1-test.4 - Heroesprofile.Uploader.Common 2.4.0.0", "2.8.1-test.4")]
    public void Parses_the_version_out_of_tags_and_version_output(string text, string expected)
    {
        Assert.Equal(expected, ReleaseVersion.Parse(text)!.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v2.8")]
    public void Returns_null_when_there_is_no_version(string text)
    {
        Assert.Null(ReleaseVersion.Parse(text));
    }

    [Theory]
    [InlineData("2.8.0-test.3", "2.8.0-test.10")]
    [InlineData("2.8.0-test.10", "2.8.0")]
    [InlineData("2.8.0", "2.8.1")]
    [InlineData("2.8.9", "2.9.0")]
    [InlineData("2.9.0", "10.0.0")]
    public void Orders_by_semver_precedence(string lower, string higher)
    {
        var a = ReleaseVersion.Parse(lower)!;
        var b = ReleaseVersion.Parse(higher)!;
        Assert.True(a < b);
        Assert.True(b > a);
    }
}
