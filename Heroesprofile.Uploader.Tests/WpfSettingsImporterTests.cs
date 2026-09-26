using Heroesprofile.Uploader.Common;
using Heroesprofile.Uploader.Desktop;
using Heroesprofile.Uploader.Desktop.Migration;
using Xunit;

namespace Heroesprofile.Uploader.Tests;

public class WpfSettingsImporterTests
{
    // The shape of the WPF app's last.config / user.config (setting names from Settings.settings).
    private static string UserConfig(params (string Name, string Value)[] settings) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <userSettings>
            <Heroesprofile.Uploader.Windows.Properties.Settings>
        {string.Join("\n", settings.Select(s => $"""      <setting name="{s.Name}" serializeAs="String"><value>{s.Value}</value></setting>"""))}
            </Heroesprofile.Uploader.Windows.Properties.Settings>
          </userSettings>
        </configuration>
        """;

    private static string FakeUnprotect(string base64) => "decrypted:" + base64;

    [Fact]
    public void Imports_every_setting_the_WPF_app_had()
    {
        var xml = UserConfig(
            ("UpgradeRequired", "False"),
            ("AutoUpdate", "False"),
            ("UpdateRepository", "https://github.com/someone/fork"), // the WPF app stored a full URL
            ("WindowTop", "120"),
            ("WindowLeft", "80"),
            ("MinimizeToTray", "True"),
            ("WindowHeight", "650"),
            ("WindowWidth", "720"),
            ("DeleteAfterUpload", "PTR, Ai"),
            ("Theme", "Default"),
            ("AllowPreReleases", "True"),
            ("ApplicationVersion", "2.9.0.0"),
            ("PreMatchPage", "True"),
            ("PostMatchPage", "True"),
            ("WebhookUrl", "https://discord.com/api/webhooks/1/abc"),
            ("ReplayPath", @"E:\Games\Heroes of the Storm\Accounts"),
            ("TwitchUploaderKey", "QUJD"),
            ("TwitchExtension", "True"));

        var config = WpfSettingsImporter.FromUserConfig(xml, FakeUnprotect);

        Assert.False(config.AutoUpdate);
        Assert.Equal("someone/fork", config.UpdateRepository);
        Assert.Equal(120, config.WindowTop);
        Assert.Equal(80, config.WindowLeft);
        Assert.Equal(650, config.WindowHeight);
        Assert.Equal(720, config.WindowWidth);
        Assert.True(config.MinimizeToTray);
        Assert.Equal(DeleteFiles.PTR | DeleteFiles.Ai, config.DeleteAfterUpload);
        Assert.Equal(AppConfig.LightTheme, config.Theme);
        Assert.True(config.AllowPreReleases);
        Assert.True(config.PreMatchPage);
        Assert.True(config.PostMatchPage);
        Assert.Equal("https://discord.com/api/webhooks/1/abc", config.WebhookUrl);
        Assert.Equal(@"E:\Games\Heroes of the Storm\Accounts", config.ReplayPath);
        Assert.Equal("decrypted:QUJD", config.TwitchUploaderKey);
        Assert.True(config.TwitchExtension);
    }

    [Fact]
    public void An_empty_WPF_replay_path_means_the_default_folder()
    {
        var config = WpfSettingsImporter.FromUserConfig(UserConfig(("ReplayPath", ""), ("TwitchUploaderKey", "")), FakeUnprotect);

        Assert.Null(config.ReplayPath);
        Assert.Equal("", config.TwitchUploaderKey);
    }

    [Fact]
    public void Values_that_do_not_parse_keep_the_defaults()
    {
        var config = WpfSettingsImporter.FromUserConfig(
            UserConfig(("AutoUpdate", "maybe"), ("WindowWidth", "wide"), ("DeleteAfterUpload", "Everything"), ("Theme", "")),
            FakeUnprotect);

        Assert.True(config.AutoUpdate);
        Assert.Equal(700, config.WindowWidth);
        Assert.Equal(DeleteFiles.None, config.DeleteAfterUpload);
        Assert.Equal(AppConfig.DarkTheme, config.Theme);
    }

    [Fact]
    public void Rejects_a_file_that_is_not_the_WPF_apps_settings()
    {
        Assert.Throws<FormatException>(() =>
            WpfSettingsImporter.FromUserConfig("<configuration><userSettings /></configuration>", FakeUnprotect));
    }
}
