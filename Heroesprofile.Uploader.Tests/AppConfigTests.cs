using Heroesprofile.Uploader.Desktop;
using Xunit;

namespace Heroesprofile.Uploader.Tests;

[Collection(StoredTwitchKey.Collection)]
public class AppConfigTests
{
    [Fact]
    public void Reads_the_Linux_apps_old_Prefix_setting_as_ReplayPath()
    {
        var config = AppConfig.FromJson("""{ "Prefix": "/home/me/Games/battlenet", "PreMatchPage": true }""");

        Assert.Equal("/home/me/Games/battlenet", config.ReplayPath);
        Assert.True(config.PreMatchPage);
    }

    [Theory]
    [InlineData("""{ "Prefix": "/old", "ReplayPath": "/new" }""")]
    [InlineData("""{ "ReplayPath": "/new", "Prefix": "/old" }""")]
    public void An_explicit_ReplayPath_wins_over_the_old_Prefix(string json)
    {
        Assert.Equal("/new", AppConfig.FromJson(json).ReplayPath);
    }

    [Fact]
    public void Saves_ReplayPath_and_never_writes_Prefix_back()
    {
        var json = AppConfig.FromJson("""{ "Prefix": "/home/me/prefix" }""").ToJson();

        Assert.Contains("\"ReplayPath\": \"/home/me/prefix\"", json);
        Assert.DoesNotContain("\"Prefix\"", json);
    }

    [Theory]
    [InlineData("https://github.com/Heroes-Profile/HeroesProfile.Uploader", "Heroes-Profile/HeroesProfile.Uploader")] // how the WPF app stored it
    [InlineData("https://github.com/someone/fork/", "someone/fork")]
    [InlineData("github.com/someone/fork.git", "someone/fork")]
    [InlineData("someone/fork", "someone/fork")]
    [InlineData("", AppConfig.DefaultUpdateRepository)]
    [InlineData("not a repository", AppConfig.DefaultUpdateRepository)]
    public void Update_repository_is_always_owner_slash_repo(string stored, string expected)
    {
        Assert.Equal(expected, AppConfig.FromJson($$"""{ "UpdateRepository": "{{stored}}" }""").UpdateRepository);
    }
}
