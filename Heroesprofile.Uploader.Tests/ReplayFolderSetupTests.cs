using Heroesprofile.Uploader.Desktop;
using Xunit;

namespace Heroesprofile.Uploader.Tests;

public sealed class ReplayFolderSetupTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("hp-replayfolder-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Make(params string[] segments)
    {
        var path = Path.Combine(new[] { _root }.Concat(segments).ToArray());
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void Windows_and_macOS_use_the_default_folder_when_nothing_is_configured()
    {
        var accounts = Make("Documents", "Heroes of the Storm", "Accounts");

        var folders = ReplayFolderSetup.Resolve(FakePlatform.WindowsLike(accounts), "", out var error);

        Assert.Null(error);
        Assert.Equal(accounts, folders!.Accounts);
        Assert.Null(folders.BattleLobby); // LiveMonitor's own default: the OS temp folder
    }

    [Fact]
    public void Windows_and_macOS_report_a_missing_default_folder()
    {
        var missing = Path.Combine(_root, "Documents", "Heroes of the Storm", "Accounts");

        var folders = ReplayFolderSetup.Resolve(FakePlatform.WindowsLike(missing), null!, out var error);

        Assert.Null(folders);
        Assert.Contains(missing, error);
    }

    [Fact]
    public void Windows_and_macOS_use_a_configured_accounts_folder_as_is()
    {
        var accounts = Make("Elsewhere", "Accounts");

        var folders = ReplayFolderSetup.Resolve(FakePlatform.WindowsLike(defaultReplayFolder: null), "  " + accounts + " ", out _);

        Assert.Equal(accounts, folders!.Accounts);
    }

    [Fact]
    public void Windows_and_macOS_report_a_configured_folder_that_does_not_exist()
    {
        var folders = ReplayFolderSetup.Resolve(FakePlatform.WindowsLike(defaultReplayFolder: null), Path.Combine(_root, "nope"), out var error);

        Assert.Null(folders);
        Assert.Contains("doesn't exist", error);
    }

    [Fact]
    public void Linux_needs_a_prefix_when_nothing_is_configured()
    {
        var folders = ReplayFolderSetup.Resolve(FakePlatform.LinuxLike(), "", out var error);

        Assert.Null(folders);
        Assert.Contains("prefix", error);
    }

    [Fact]
    public void Linux_finds_the_accounts_and_temp_folders_inside_a_prefix()
    {
        var accounts = Make("drive_c", "users", "steamuser", "Documents", "Heroes of the Storm", "Accounts");
        var temp = Make("drive_c", "users", "steamuser", "AppData", "Local", "Temp");

        var folders = ReplayFolderSetup.Resolve(FakePlatform.LinuxLike(), _root, out _);

        Assert.Equal(accounts, folders!.Accounts);
        Assert.Equal(temp, folders.BattleLobby);
    }

    [Fact]
    public void Linux_reports_a_prefix_without_heroes_of_the_storm()
    {
        Make("drive_c", "users", "steamuser", "Documents");

        var folders = ReplayFolderSetup.Resolve(FakePlatform.LinuxLike(), _root, out var error);

        Assert.Null(folders);
        Assert.Contains(_root, error);
    }
}
