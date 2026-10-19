#if WINDOWS
using System.Diagnostics;
using Heroesprofile.Uploader.RankCapture;
using Xunit;

namespace Heroesprofile.Uploader.RankCapture.Tests;

/// <summary>
/// The HDR capture path (WindowCapture's scRGB capture, read back through Direct3D and converted by ScRgb).
/// Any display can be captured in scRGB, so on an SDR one the HDR path must give the same picture as the
/// ordinary capture - which checks everything but a real HDR display's brightness. Windows only.
/// </summary>
public class WindowsHdrCaptureTests(ITestOutputHelper output)
{
    [Fact]
    public void Displays_say_what_they_are()
    {
        Assert.Equal("SDR", DisplayColour.Sdr.ToString());
        Assert.Equal("HDR, SDR white 200 nits", new DisplayColour(true, 200).ToString());
    }

    /// <summary>
    /// Captures a visible window both ways and compares. Explicit: it needs a desktop and a window on it.
    /// <c>Heroesprofile.Uploader.RankCapture.Tests.exe -method "*The_hdr_capture_gives_the_ordinary_capture_on_an_sdr_display" -explicit only -showliveoutput</c>
    /// </summary>
    [Fact(Explicit = true)]
    public async Task The_hdr_capture_gives_the_ordinary_capture_on_an_sdr_display()
    {
        // The biggest visible, not minimised window on the desktop.
        var window = Process.GetProcesses()
            .Select(p => p.MainWindowHandle)
            .Where(w => w != IntPtr.Zero && IsWindowVisible(w) && !IsIconic(w) && GetWindowRect(w, out _))
            .OrderByDescending(w => GetWindowRect(w, out var r) ? (long)(r.Right - r.Left) * (r.Bottom - r.Top) : 0)
            .FirstOrDefault();
        Assert.SkipWhen(window == IntPtr.Zero, "No window to capture");

        var found = DisplayColour.TryForWindow(window, out var colour);
        output.WriteLine($"Display: {(found ? colour.ToString() : "not found in the display configuration")}");
        Assert.True(found, "The window's display wasn't found in the display configuration");
        Assert.SkipWhen(colour.Hdr, "This display is HDR: the two captures are meant to differ here");

        var ordinary = await CaptureOneAsync(window, DisplayColour.Sdr);
        var scRgb = await CaptureOneAsync(window, new DisplayColour(true, ScRgb.WhiteNits));
        Assert.True(scRgb.Hdr);
        Assert.Equal((ordinary.Width, ordinary.Height), (scRgb.Width, scRgb.Height));

        long total = 0;
        var worst = 0;
        for (var y = 0; y < ordinary.Height; y++) {
            for (var x = 0; x < ordinary.Width; x++) {
                for (var c = 0; c < 3; c++) {
                    var d = Math.Abs(ordinary.Bgra[y * ordinary.Stride + x * 4 + c] - scRgb.Bgra[y * scRgb.Stride + x * 4 + c]);
                    total += d;
                    worst = Math.Max(worst, d);
                }
            }
        }
        var mean = (double)total / (ordinary.Width * ordinary.Height * 3);
        Assert.True(ordinary.Width >= 200 && ordinary.Height >= 200, $"Captured only {ordinary.Width}x{ordinary.Height}");
        output.WriteLine($"{ordinary.Width}x{ordinary.Height}: mean difference {mean:0.000} per channel, worst {worst}");
        // Rounding through half floats moves a channel by 1 at most; a moving cursor or animation in between
        // the two captures can do more in places, but not on average.
        Assert.True(mean < 1, $"The scRGB capture differs from the ordinary one by {mean:0.00} per channel on average");
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

    private static async Task<RawFrame> CaptureOneAsync(IntPtr window, DisplayColour colour)
    {
        var first = new TaskCompletionSource<RawFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (WindowCapture.Start(window, TimeSpan.FromMilliseconds(50), showOutline: true, colour, frame => first.TrySetResult(frame))) {
            var done = await Task.WhenAny(first.Task, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.True(done == first.Task, "No frame within 10 seconds (is the window minimised?)");
            return await first.Task;
        }
    }
}
#endif
