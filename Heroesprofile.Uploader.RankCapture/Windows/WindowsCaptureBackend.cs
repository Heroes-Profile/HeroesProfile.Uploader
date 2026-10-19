using NLog;
using System;
using System.Threading.Tasks;

namespace Heroesprofile.Uploader.RankCapture
{
    /// <summary>
    /// Capture on Windows: Windows' own screen capture (Windows.Graphics.Capture, the API OBS, Discord and the
    /// Game Bar use), of the game's window only - see <see cref="WindowCapture"/>.
    /// </summary>
    internal sealed class WindowsCaptureBackend : ICaptureBackend
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        public string Unsupported => WindowCapture.IsSupported ? null : "this version of Windows can't capture windows (needs Windows 10 2004 or later)";

        public async Task PrepareAsync(bool showOutline)
        {
            if (!showOutline) {
                await WindowCapture.EnsureBorderlessAccessAsync();
            }
        }

        public IDisposable Start(TimeSpan interval, bool showOutline, Action<RawFrame> onFrame)
        {
            var window = WindowCapture.FindGameWindow();
            if (window == IntPtr.Zero) {
                return null;
            }
            // An HDR display is captured in its own scRGB, which the 8-bit capture got wrong (WindowCapture).
            if (DisplayColour.TryForWindow(window, out var colour)) {
                _log.Info($"Rank reading: the game's display is {colour}");
            } else {
                _log.Info("Rank reading: couldn't tell whether the game's display is HDR - capturing it as SDR");
            }
            return WindowCapture.Start(window, interval, showOutline, colour, onFrame);
        }
    }
}
