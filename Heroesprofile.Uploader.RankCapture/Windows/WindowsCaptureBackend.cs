using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;

namespace Heroesprofile.Uploader.RankCapture
{
    /// <summary>
    /// Capture on Windows: Windows' own screen capture (Windows.Graphics.Capture, the API OBS, Discord and the
    /// Game Bar use), of the game's window only - see <see cref="WindowCapture"/>.
    /// </summary>
    internal sealed class WindowsCaptureBackend : ICaptureBackend
    {
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
            return WindowCapture.Start(window, interval, showOutline, bitmap => {
                try {
                    var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                    bitmap.CopyToBuffer(bytes.AsBuffer());
                    onFrame(new RawFrame(bitmap.PixelWidth, bitmap.PixelHeight, bitmap.PixelWidth * 4, bytes));
                }
                finally {
                    bitmap.Dispose();
                }
            });
        }
    }
}
