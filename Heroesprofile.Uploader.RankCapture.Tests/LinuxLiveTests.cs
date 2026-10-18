using System.Diagnostics;
using System.Text.Json;
using Heroesprofile.Uploader.RankCapture;
using Xunit;

namespace Heroesprofile.Uploader.RankCapture.Tests;

/// <summary>
/// The Linux rank reader end to end, without the game: a sample's loading screen is put back together, shown
/// in an X11 window titled like the game's, captured through <see cref="X11CaptureBackend"/>, cut into strips
/// the way GameCapture does, and read - ranks, and names with Tesseract. Explicit: it needs Linux, an X display
/// (Xvfb will do), Tesseract and a viewer program (HP_X11_VIEWER: run with the image's path, it must show it in
/// a window titled "Heroes of the Storm"), plus the samples.
/// </summary>
public class LinuxLiveTests(ITestOutputHelper output)
{
    [Fact(Explicit = true)]
    public async Task Reads_ranks_off_a_live_X11_window()
    {
        Samples.RequireSamples();
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Linux only");
        var viewer = Environment.GetEnvironmentVariable("HP_X11_VIEWER");
        Assert.SkipWhen(string.IsNullOrEmpty(viewer), "HP_X11_VIEWER isn't set");

        var labels = Samples.LoadLabels().Where(l => l.SampleId.StartsWith("a1aab954", StringComparison.Ordinal)).ToList();
        var game = labels[0].GameFolder;
        var files = Samples.FrameFiles(game).First();

        // The 2560 x 1440 screen the strips were cut from: left strip at the left, right strip at the right.
        var left = Png.DecodeRaw(await File.ReadAllBytesAsync(files.Left, TestContext.Current.CancellationToken));
        var right = Png.DecodeRaw(await File.ReadAllBytesAsync(files.Right, TestContext.Current.CancellationToken));
        var width = left.Width * 4;
        var screen = new byte[width * 4 * left.Height];
        for (var y = 0; y < left.Height; y++) {
            Buffer.BlockCopy(left.Bgra, y * left.Stride, screen, y * width * 4, left.Width * 4);
            Buffer.BlockCopy(right.Bgra, y * right.Stride, screen, (y * width + width - right.Width) * 4, right.Width * 4);
        }
        var screenPath = Path.Combine(Path.GetTempPath(), "hp-loading-screen.png");
        await File.WriteAllBytesAsync(screenPath, Png.EncodeStrip(new RawFrame(width, left.Height, width * 4, screen), 0, width), TestContext.Current.CancellationToken);

        using var shown = Process.Start(new ProcessStartInfo(viewer!, screenPath) { UseShellExecute = false })!;
        try {
            // The window, found and read directly first - so a failure says which step it was.
            await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            var display = X11CaptureBackend.X.XOpenDisplay(IntPtr.Zero);
            Assert.NotEqual(IntPtr.Zero, display);
            try {
                var window = X11CaptureBackend.X.FindWindow(display, "Heroes of the Storm");
                output.WriteLine($"Window found: {window != IntPtr.Zero}");
                Assert.NotEqual(IntPtr.Zero, window);
                var direct = X11CaptureBackend.X.Capture(display, window);
                output.WriteLine($"Read directly: {(direct == null ? "nothing" : $"{direct.Width} x {direct.Height}")}");
                Assert.NotNull(direct);
            }
            finally {
                X11CaptureBackend.X.XCloseDisplay(display);
            }

            // Capture as the app does, until a frame comes.
            var backend = CaptureBackends.Current;
            Assert.Null(backend.Unsupported);
            var got = new TaskCompletionSource<RawFrame>();
            using (backend.Start(TimeSpan.FromSeconds(1), false, f => got.TrySetResult(f))) {
                var first = await Task.WhenAny(got.Task, Task.Delay(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
                Assert.True(first == got.Task, "No frame captured from the X11 window");
            }
            var frame = await got.Task;
            output.WriteLine($"Captured {frame.Width} x {frame.Height}");
            Assert.Equal((width, left.Height), (frame.Width, frame.Height));

            // Cut and read the way GameCapture and RankReader do.
            var strip = (int)Math.Round(frame.Width * 0.25);
            var leftStrip = Png.Decode(Png.EncodeStrip(frame, 0, strip));
            var rightStrip = Png.Decode(Png.EncodeStrip(frame, frame.Width - strip, strip));
            var cards = await PlayerRanks.ReadCardsAsync(new[] { (leftStrip, rightStrip) }, RankTemplates.Shipped);
            Assert.NotNull(cards);

            foreach (var label in labels.Where(l => l.Expected != null)) {
                var card = cards.Single(c => c.Rank.Side == label.Side && c.Rank.Slot == label.Slot);
                output.WriteLine($"{card.Rank}: names read {string.Join(" | ", card.Text.Names)}; heroes {string.Join(" | ", card.Text.Heroes)}");
                Assert.Equal(label.Expected!.Value.League, card.Rank.League);
                Assert.Equal(label.Expected.Value.Division, card.Rank.Division);
            }

            // Tesseract reads the names well enough to match the lobby's players.
            using var meta = JsonDocument.Parse(File.ReadAllText(Path.Combine(game, "meta.json")));
            var players = meta.RootElement.GetProperty("meta").GetProperty("players").EnumerateArray()
                .Select(p => new PlayerMatcher.LobbyPlayer(p.GetProperty("name").GetString() ?? "", p.GetProperty("team").GetInt32()))
                .ToList();
            var matches = PlayerMatcher.MatchCards(cards.Select(c => c.Text).ToList(), players)!;
            var identified = matches.Count(m => m.Identified);
            output.WriteLine($"{identified} of 10 cards identified by name with Tesseract ({string.Join(", ", CardText.Languages)})");
            Assert.True(identified >= 8, $"Only {identified} of 10 names read well enough");
        }
        finally {
            try {
                shown.Kill();
            }
            catch (InvalidOperationException) {
            }
        }
    }
}
