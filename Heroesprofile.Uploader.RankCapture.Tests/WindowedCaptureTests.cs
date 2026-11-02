using Heroesprofile.Uploader.RankCapture;
using Xunit;

namespace Heroesprofile.Uploader.RankCapture.Tests;

/// <summary>
/// A game in a window is captured with its title bar and borders around the game's image. Read as it was, the
/// cards sat lower and smaller than the reader expects and every one came out Unranked; cut to the game's own
/// image (what WindowCapture now does, see ClientArea) they read like any other game.
/// </summary>
public class WindowedCaptureTests(ITestOutputHelper output)
{
    /// <summary>
    /// Captures from 3.0.5/3.0.7 that read all ten cards Unranked, and where the game's image sits in them:
    /// sample id, the title bar and borders measured on the frames (top, left, right, bottom, in pixels), and
    /// the cards with no division badge on them (checked by eye: plain purple frame, no shield).
    /// </summary>
    private static readonly (string Sample, int Top, int Left, int Right, int Bottom, string[] NoBadge)[] WindowedGames = {
        ("63f0c1a7", 23, 0, 0, 0, new string[0]),  // 2560 x 1400: a maximised window, title bar only
        ("4e873aa5", 31, 1, 1, 1, new[] { "L5" }), // 1832 x 1033: title bar and a 1-pixel border
    };

    [Fact]
    public async Task A_windowed_game_reads_once_its_title_bar_and_borders_are_cut_off()
    {
        Samples.RequireSamples();
        var templates = RankTemplates.Shipped!;
        foreach (var (sample, top, left, right, bottom, noBadge) in WindowedGames) {
            var folder = Directory.GetDirectories(Samples.Folder, "2*").SelectMany(d => Directory.GetDirectories(d, sample + "*")).FirstOrDefault();
            if (folder == null) {
                Assert.Skip($"Windowed sample {sample} isn't here");
            }
            var asCaptured = new List<(Pixels, Pixels)>();
            var cut = new List<(Pixels, Pixels)>();
            foreach (var files in Samples.FrameFiles(folder)) {
                var (l, r) = await Samples.DecodeAsync(files);
                asCaptured.Add((l, r));
                // The strips are the window's outer quarters: the left one has the left border, the right one the right.
                cut.Add((Crop(l, left, top, 0, bottom), Crop(r, 0, top, right, bottom)));
            }

            var before = LoadingScreenReader.ReadGame(asCaptured.Where(f => LoadingScreenReader.IsLoadingScreen(f.Item1, f.Item2)), templates);
            var after = LoadingScreenReader.ReadGame(cut.Where(f => LoadingScreenReader.IsLoadingScreen(f.Item1, f.Item2)), templates);
            output.WriteLine($"{sample} as captured: {(before == null ? "not the loading screen" : string.Join(", ", before.Select(c => c.Rank)))}");
            output.WriteLine($"{sample} game image only: {(after == null ? "not the loading screen" : string.Join(", ", after.Select(c => c.Rank)))}");

            Assert.NotNull(after);
            foreach (var c in after) {
                var card = $"{(c.Side == Side.Left ? "L" : "R")}{c.Slot + 1}";
                if (noBadge.Contains(card)) {
                    Assert.Equal(League.Unranked, c.League);
                } else {
                    Assert.True(c.League is not (League.Unranked or League.Unknown), $"{sample} {card} read {c.Rank}");
                }
            }
        }
    }

    private static Pixels Crop(Pixels strip, int left, int top, int right, int bottom) =>
        strip.Sample(left, top, 1, strip.Width - left - right, strip.Height - top - bottom);

#if WINDOWS
    [Fact]
    public void The_game_image_is_the_window_without_its_title_bar_and_borders()
    {
        // A 1832 x 1033 window at (100, 50) on screen: 30-pixel title bar and 1-pixel border round a 1830 x 1001 game.
        var window = new ClientArea.Rect(100, 50, 1932, 1083);
        var game = new ClientArea.Rect(101, 81, 1931, 1082);
        Assert.Equal(new ClientArea.Rect(1, 31, 1831, 1032), ClientArea.Within(1832, 1033, window, game));

        // Fullscreen: nothing around it, so the whole frame.
        var screen = new ClientArea.Rect(0, 0, 2560, 1440);
        Assert.Equal(new ClientArea.Rect(0, 0, 2560, 1440), ClientArea.Within(2560, 1440, screen, screen));
    }

    [Fact]
    public void A_capture_that_isnt_the_window_is_left_as_it_is()
    {
        var window = new ClientArea.Rect(100, 50, 1932, 1083);
        var game = new ClientArea.Rect(101, 81, 1931, 1082);
        // A frame of another size (a different DPI scale): the rectangles don't describe it.
        Assert.Null(ClientArea.Within(2290, 1291, window, game));
        // The game's rectangle reaching outside the window's.
        Assert.Null(ClientArea.Within(1832, 1033, window, game with { Bottom = 1100 }));
        // Far smaller than the window: not a title bar and borders.
        Assert.Null(ClientArea.Within(1832, 1033, window, new ClientArea.Rect(500, 500, 900, 900)));
    }

    /// <summary>
    /// Captures a real window with a title bar and checks the frame comes out the size of its client area.
    /// Explicit: it needs a desktop with such a window (not minimised, not maximised over the title bar).
    /// <c>Heroesprofile.Uploader.RankCapture.Tests.exe -method "*A_window_is_captured_without_its_title_bar" -explicit only -showliveoutput</c>
    /// </summary>
    [Fact(Explicit = true)]
    public async Task A_window_is_captured_without_its_title_bar()
    {
        var window = System.Diagnostics.Process.GetProcesses()
            .Select(p => p.MainWindowHandle)
            .Where(w => w != IntPtr.Zero && IsWindowVisible(w) && !IsIconic(w)
                && GetWindowRect(w, out var r) && GetClientRect(w, out var c) && c.Bottom >= 200 && (r.Bottom - r.Top) - c.Bottom >= 20)
            .OrderByDescending(w => GetClientRect(w, out var c) ? (long)c.Right * c.Bottom : 0)
            .FirstOrDefault();
        Assert.SkipWhen(window == IntPtr.Zero, "No window with a title bar to capture");
        GetWindowRect(window, out var outer);
        GetClientRect(window, out var client);

        var first = new TaskCompletionSource<RawFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (WindowCapture.Start(window, TimeSpan.FromMilliseconds(50), showOutline: true, DisplayColour.Sdr, frame => first.TrySetResult(frame))) {
            var done = await Task.WhenAny(first.Task, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.True(done == first.Task, "No frame within 10 seconds");
        }
        var frame = await first.Task;
        output.WriteLine($"Window {outer.Right - outer.Left} x {outer.Bottom - outer.Top}, client {client.Right} x {client.Bottom}, captured {frame.Width} x {frame.Height}");
        Assert.Equal((client.Right, client.Bottom), (frame.Width, frame.Height));
    }

    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out Rect rect);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr window, out Rect rect);
#endif
}
