using System;
using System.Threading.Tasks;

namespace Heroesprofile.Uploader.RankCapture
{
    /// <summary>One captured image of the game window: 32-bit BGRA rows (the alpha byte is ignored).</summary>
    internal sealed record RawFrame(int Width, int Height, int Stride, byte[] Bgra);

    /// <summary>
    /// How this OS captures the game's window (<see cref="CaptureBackends.Current"/>): Windows' own capture
    /// (Windows/WindowsCaptureBackend), X11 on Linux (<see cref="X11CaptureBackend"/>), or the bundled helper
    /// on macOS (<see cref="MacCaptureBackend"/>). Only ever the game's own window, and only in memory.
    /// </summary>
    internal interface ICaptureBackend
    {
        /// <summary>Why this machine can't capture the game, or null when it can.</summary>
        string Unsupported { get; }

        /// <summary>
        /// Before a game's capture: on Windows, asks to capture without the yellow outline (unless
        /// <paramref name="showOutline"/>); on macOS, makes sure the app may record the screen.
        /// </summary>
        Task PrepareAsync(bool showOutline);

        /// <summary>
        /// Starts capturing the game's window, handing <paramref name="onFrame"/> (on a worker thread) a frame
        /// at most every <paramref name="interval"/>; dispose to stop. Null when the game's window isn't up
        /// (yet) - the caller tries again.
        /// </summary>
        IDisposable Start(TimeSpan interval, bool showOutline, Action<RawFrame> onFrame);
    }

    /// <summary>The capture for the OS the app runs on.</summary>
    internal static class CaptureBackends
    {
        private static readonly Lazy<ICaptureBackend> _current = new Lazy<ICaptureBackend>(Create);

        public static ICaptureBackend Current => _current.Value;

        private static ICaptureBackend Create()
        {
#if WINDOWS_CAPTURE
            if (OperatingSystem.IsWindows()) {
                return new WindowsCaptureBackend();
            }
#endif
            if (OperatingSystem.IsLinux()) {
                return new X11CaptureBackend();
            }
            if (OperatingSystem.IsMacOS()) {
                return new MacCaptureBackend();
            }
            return new NoCaptureBackend();
        }

        private sealed class NoCaptureBackend : ICaptureBackend
        {
            public string Unsupported => "this build can't capture the game window on this system";
            public Task PrepareAsync(bool showOutline) => Task.CompletedTask;
            public IDisposable Start(TimeSpan interval, bool showOutline, Action<RawFrame> onFrame) => null;
        }
    }

    /// <summary>
    /// Capture by asking for a frame every so often (Linux and macOS have no stream of frames to hand out):
    /// a background loop calling <c>grab</c> every <c>interval</c> until disposed. A grab that fails, or finds
    /// no window, is skipped.
    /// </summary>
    internal sealed class PollingCapture : IDisposable
    {
        private readonly System.Threading.CancellationTokenSource _stop = new System.Threading.CancellationTokenSource();

        public PollingCapture(TimeSpan interval, Func<RawFrame> grab, Action<RawFrame> onFrame, Action<Exception> onError)
        {
            Task.Run(async () => {
                while (!_stop.IsCancellationRequested) {
                    var started = DateTime.UtcNow;
                    try {
                        if (grab() is RawFrame frame && !_stop.IsCancellationRequested) {
                            onFrame(frame);
                        }
                    }
                    catch (Exception ex) {
                        onError(ex);
                    }
                    var wait = interval - (DateTime.UtcNow - started);
                    try {
                        await Task.Delay(wait > TimeSpan.Zero ? wait : TimeSpan.Zero, _stop.Token);
                    }
                    catch (OperationCanceledException) {
                    }
                }
            });
        }

        public void Dispose() => _stop.Cancel();
    }
}
