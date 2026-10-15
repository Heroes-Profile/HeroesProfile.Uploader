using Heroesprofile.Uploader.Common;
using Heroesprofile.Uploader.Desktop.Platform;
using Xunit;

namespace Heroesprofile.Uploader.Tests;

/// <summary>Runs on each CI OS, so each one checks its own real platform.</summary>
public class PlatformTests
{
    private static readonly IPlatform Platform = Platforms.Current;

    [Fact]
    public void Picks_the_platform_for_the_OS_it_runs_on()
    {
        var expected = OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" : "Linux";
        Assert.Equal(expected, Platform.Name);
    }

    [Fact]
    public void Only_Linux_goes_through_a_Wine_prefix()
    {
        Assert.Equal(OperatingSystem.IsLinux(), Platform.ReplayPathIsWinePrefix);
        Assert.Equal(OperatingSystem.IsLinux(), Platform.DefaultReplayFolder == null);
    }

    [Fact]
    public void Default_replay_folder_is_the_games_own_accounts_folder()
    {
        if (OperatingSystem.IsLinux()) {
            return;
        }
        Assert.EndsWith(Path.Combine("Heroes of the Storm", "Accounts"), Platform.DefaultReplayFolder);
        if (OperatingSystem.IsMacOS()) {
            Assert.Contains(Path.Combine("Library", "Application Support", "Blizzard"), Platform.DefaultReplayFolder);
        }
    }

    [Fact]
    public void Windows_keeps_its_data_apart_from_the_WPF_app()
    {
        if (!OperatingSystem.IsWindows()) {
            return;
        }
        // Not App.SettingsDir (%APPDATA%\Heroesprofile): the WPF app's uninstaller deletes that folder.
        // (A fresh WindowsPlatform: the tests' own Platforms.Current points at a scratch folder.)
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        Assert.Equal(Path.Combine(appData, "HeroesProfileUploader"), new WindowsPlatform().DataDir);
        Assert.NotEqual(Path.Combine(appData, "Heroesprofile"), new WindowsPlatform().DataDir, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_home_override_moves_config_data_and_socket_together()
    {
        Assert.Equal(TestHome.Folder, Platform.ConfigDir);
        Assert.Equal(TestHome.Folder, Platform.DataDir);
        Assert.Equal(Path.Combine(TestHome.Folder, "instance.sock"), Platform.SingleInstanceSocketPath);
    }

    [Fact]
    public void Only_macOS_watches_somewhere_other_than_the_temp_folder_for_the_battle_lobby()
    {
        if (!OperatingSystem.IsMacOS()) {
            Assert.Null(Platform.DefaultBattleLobbyFolder);
            return;
        }
        // The parent of $TMPDIR (/var/folders/<xx>/<hash>/T/), so its T/ and C/ are both covered.
        Assert.Equal(Path.GetDirectoryName(Path.GetTempPath().TrimEnd('/')), Platform.DefaultBattleLobbyFolder);
        Assert.StartsWith(Platform.DefaultBattleLobbyFolder!, Path.GetTempPath());
    }

    [Fact]
    public void Only_Linux_and_macOS_wait_for_replays_to_finish_writing()
    {
        Assert.Equal(!OperatingSystem.IsWindows(), Platform.UseSettledMonitor);
    }

    [Fact]
    public void Commons_default_replay_path_uses_the_OS_path_separator()
    {
        Assert.EndsWith(Path.Combine("Heroes of the Storm", "Accounts"), ReplayLocation.DefaultPath);
    }
}
