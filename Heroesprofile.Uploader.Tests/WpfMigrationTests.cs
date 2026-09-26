using System.Text;
using Heroesprofile.Uploader.Desktop.Migration;
using Xunit;

namespace Heroesprofile.Uploader.Tests;

/// <summary>
/// Moving over from the WPF app, entirely in scratch folders - never the real %APPDATA%\Heroesprofile
/// or %LOCALAPPDATA%\Heroesprofile.
/// </summary>
public sealed class WpfMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"hp-migration-tests-{Guid.NewGuid():N}");

    public WpfMigrationTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Sub(params string[] parts) => Path.Combine(new[] { _root }.Concat(parts).ToArray());

    private static void Touch(string path, string content = "")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void Upload_history_is_copied_into_the_new_folder()
    {
        Touch(Sub("old", "replays_v8.xml"), "old history");

        Assert.True(WpfMigration.CopyHistory(Sub("old"), Sub("new")));
        Assert.Equal("old history", File.ReadAllText(Sub("new", "replays_v8.xml")));
        Assert.True(File.Exists(Sub("old", "replays_v8.xml")));
    }

    [Fact]
    public void Upload_history_never_overwrites_the_new_apps_own()
    {
        Touch(Sub("old", "replays_v8.xml"), "old history");
        Touch(Sub("new", "replays_v8.xml"), "new history");

        Assert.False(WpfMigration.CopyHistory(Sub("old"), Sub("new")));
        Assert.Equal("new history", File.ReadAllText(Sub("new", "replays_v8.xml")));
    }

    [Fact]
    public void No_old_history_means_nothing_to_copy()
    {
        Assert.False(WpfMigration.CopyHistory(Sub("old"), Sub("new")));
        Assert.False(Directory.Exists(Sub("new")));
    }

    [Fact]
    public void Old_app_counts_as_installed_only_with_its_updater_and_app()
    {
        var install = Sub("Heroesprofile");
        Directory.CreateDirectory(install);
        Assert.False(LegacyApp.IsInstalledIn(install));

        Touch(Path.Combine(install, "Update.exe"));
        Assert.False(LegacyApp.IsInstalledIn(install));

        Directory.CreateDirectory(Path.Combine(install, "app-2.9.0"));
        Assert.True(LegacyApp.IsInstalledIn(install));
    }

    [Fact]
    public void Leftover_program_files_go_but_settings_folders_stay()
    {
        var install = Sub("Heroesprofile");
        Touch(Path.Combine(install, "Update.exe"));
        Touch(Path.Combine(install, "Heroesprofile.Uploader.exe"));
        Touch(Path.Combine(install, "app-2.9.0", "Heroesprofile.Uploader.exe"));
        Touch(Path.Combine(install, "packages", "RELEASES"));
        Touch(Path.Combine(install, "SquirrelSetup.log"));
        Touch(Path.Combine(install, "Heroesprofile.Uploader.exe_Url_abc", "2.9.0.0", "user.config"));

        LegacyApp.RemoveProgramFiles(install);

        Assert.Equal(new[] { "Heroesprofile.Uploader.exe_Url_abc" },
            Directory.EnumerateFileSystemEntries(install).Select(Path.GetFileName));
    }

    [Fact]
    public void Only_shortcuts_into_the_old_install_are_removed()
    {
        var install = Sub("Heroesprofile");
        var startup = Sub("Startup");
        var desktop = Sub("Desktop");
        var programs = Sub("Programs");

        // A .lnk keeps its target as text - UTF-16 here, like the real ones.
        var oldTarget = Encoding.Unicode.GetBytes(Path.Combine(install, "Heroesprofile.Uploader.exe"));
        var newTarget = Encoding.Unicode.GetBytes(Sub("Heroesprofile.Uploader", "current", "HeroesProfileUploader.exe"));
        Directory.CreateDirectory(startup);
        Directory.CreateDirectory(desktop);
        Directory.CreateDirectory(Path.Combine(programs, "Heroes Profile"));
        File.WriteAllBytes(Path.Combine(startup, "Heroesprofile Uploader.lnk"), oldTarget);
        File.WriteAllBytes(Path.Combine(programs, "Heroes Profile", "Heroesprofile Uploader.lnk"), oldTarget);
        File.WriteAllBytes(Path.Combine(desktop, "Heroesprofile Uploader.lnk"), newTarget);

        LegacyApp.RemoveShortcuts(install, new[] { desktop, startup }, programs);

        Assert.False(File.Exists(Path.Combine(startup, "Heroesprofile Uploader.lnk")));
        Assert.False(Directory.Exists(Path.Combine(programs, "Heroes Profile")));
        Assert.True(File.Exists(Path.Combine(desktop, "Heroesprofile Uploader.lnk")));
    }
}
