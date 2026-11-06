using Heroesprofile.Uploader.Desktop;
using Xunit;

namespace Heroesprofile.Uploader.Tests;

// Installing a downloaded update at start stops every running copy and restarts with this launch's
// arguments, so only a GUI launch with nothing else running may do it.
public class StagedUpdateTests
{
    private static string[] Args(string commandLine) => commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    [Theory]
    [InlineData("")]
    [InlineData("--minimized")]
    public void A_gui_launch_with_no_other_copy_running_installs_it(string commandLine)
    {
        Assert.True(Program.AppliesStagedUpdate(Args(commandLine), anotherCopyRunning: () => false));
    }

    [Theory]
    [InlineData("")]
    [InlineData("--minimized")]
    public void A_gui_launch_while_another_copy_runs_leaves_it(string commandLine)
    {
        Assert.False(Program.AppliesStagedUpdate(Args(commandLine), anotherCopyRunning: () => true));
    }

    [Theory]
    [InlineData("run")]
    [InlineData("--prefix /games/hots run")]
    [InlineData("scan --dry-run")]
    [InlineData("--version")]
    [InlineData("--help")]
    [InlineData("-h")]
    public void A_command_never_installs_it(string commandLine)
    {
        Assert.False(Program.AppliesStagedUpdate(Args(commandLine), anotherCopyRunning: () => false));
    }
}
