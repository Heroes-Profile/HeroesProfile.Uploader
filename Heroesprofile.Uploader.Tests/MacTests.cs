using System.Xml.Linq;
using Heroesprofile.Uploader.Desktop;
using Heroesprofile.Uploader.Desktop.Platform;
using Xunit;

namespace Heroesprofile.Uploader.Tests;

public class MacTests
{
    [Fact]
    public void Finds_the_app_bundle_the_executable_runs_from()
    {
        Assert.Equal("/Applications/Heroes Profile Uploader.app",
            MacLaunchAgent.AppBundleOf("/Applications/Heroes Profile Uploader.app/Contents/MacOS/heroesprofile-uploader"));
    }

    [Theory]
    [InlineData("/Users/me/Downloads/heroesprofile-uploader")]
    [InlineData("/Users/me/dev/Contents/MacOS/heroesprofile-uploader")] // right layout, but not inside an .app
    public void A_bare_executable_is_not_an_app_bundle(string exe)
    {
        Assert.Null(MacLaunchAgent.AppBundleOf(exe));
    }

    [Fact]
    public void The_launch_agent_is_a_valid_plist_that_starts_minimized_at_login()
    {
        var plist = MacLaunchAgent.Plist("com.heroesprofile.uploader",
            new[] { "/usr/bin/open", "-a", "/Applications/Heroes & Profile.app", "--args", "--minimized" });

        var dict = XDocument.Parse(plist).Root!.Element("dict")!;
        var keys = dict.Elements("key").Select(k => k.Value).ToList();
        Assert.Equal(new[] { "Label", "ProgramArguments", "RunAtLoad", "ProcessType" }, keys);
        Assert.Equal("com.heroesprofile.uploader", dict.Elements().ElementAt(1).Value);
        Assert.Equal(
            new[] { "/usr/bin/open", "-a", "/Applications/Heroes & Profile.app", "--args", "--minimized" },
            dict.Element("array")!.Elements("string").Select(s => s.Value));
        Assert.NotNull(dict.Element("true"));
    }

    [Fact]
    public void On_macOS_the_Twitch_key_goes_to_the_Keychain_when_it_can()
    {
        if (!OperatingSystem.IsMacOS()) {
            return;
        }
        const string key = "twitch-uploader-key-mac";
        var json = new AppConfig { TwitchUploaderKey = key }.ToJson();
        try {
            // A CI runner may have no usable keychain; then the key stays in the 0600 file instead.
            if (json.Contains("\"keychain:\"")) {
                Assert.DoesNotContain(key, json);
            }
            Assert.Equal(key, AppConfig.FromJson(json).TwitchUploaderKey);
        }
        finally {
            // Clearing the key removes the Keychain item this test made (it's keyed to the scratch home).
            new AppConfig { TwitchUploaderKey = "" }.ToJson();
        }
    }
}
