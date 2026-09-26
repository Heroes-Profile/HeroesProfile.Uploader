using Heroesprofile.Uploader.Desktop;
using Xunit;

namespace Heroesprofile.Uploader.Tests;

public sealed class WinePrefixTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("hp-wineprefix-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Make(params string[] segments)
    {
        var path = Path.Combine(new[] { _root }.Concat(segments).ToArray());
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void Finds_accounts_under_a_plain_prefix()
    {
        var accounts = Make("drive_c", "users", "steamuser", "Documents", "Heroes of the Storm", "Accounts");
        Assert.Equal(accounts, WinePrefix.FindAccounts(_root));
    }

    [Fact]
    public void Finds_accounts_under_a_proton_compatdata_folder()
    {
        var accounts = Make("pfx", "drive_c", "users", "steamuser", "Documents", "Heroes of the Storm", "Accounts");
        Assert.Equal(accounts, WinePrefix.FindAccounts(_root));
    }

    [Fact]
    public void Accepts_the_accounts_folder_itself()
    {
        var accounts = Make("Accounts");
        Assert.Equal(accounts, WinePrefix.FindAccounts(accounts));
    }

    [Fact]
    public void Finds_the_prefix_temp_folder()
    {
        var temp = Make("drive_c", "users", "steamuser", "AppData", "Local", "Temp");
        Assert.Equal(temp, WinePrefix.FindTemp(_root));
    }

    [Fact]
    public void Returns_null_when_nothing_matches()
    {
        Make("drive_c", "users", "steamuser", "Documents");
        Assert.Null(WinePrefix.FindAccounts(_root));
        Assert.Null(WinePrefix.FindTemp(_root));
        Assert.Null(WinePrefix.FindAccounts(Path.Combine(_root, "missing")));
    }
}
