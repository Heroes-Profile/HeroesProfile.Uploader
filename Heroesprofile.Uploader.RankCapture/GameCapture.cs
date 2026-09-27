using Heroes.ReplayParser;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;

namespace Heroesprofile.Uploader.RankCapture
{
    /// <summary>
    /// One game, from its lobby file to its first storm save. Captures the game window every few seconds -
    /// the draft (in draft modes) and then the loading screen - and keeps only the player-card strips of
    /// the last <see cref="FramesKept"/> frames, in memory. The first storm save marks the end of the loading
    /// screen, so those last frames are the loading screen's; <see cref="RankReader"/> then sends them (Storm
    /// League) or drops them (any other mode). Gives up and drops everything after <see cref="MaxWait"/>.
    /// </summary>
    internal sealed class GameCapture : IDisposable
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(8); // a full draft plus a slow load
        private const int FramesKept = 5;

        // The 10 player cards sit along the left and right edges of the loading screen; a quarter of the
        // width on each side covers them with room to spare (exact positions come from the samples).
        private const double StripWidth = 0.25;

        public Replay Lobby { get; }
        public DateTime LobbyAt { get; } = DateTime.UtcNow;

        private readonly object _lock = new object();
        private readonly Queue<CapturedFrame> _frames = new Queue<CapturedFrame>();
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private WindowCapture _capture;

        private GameCapture(Replay lobby)
        {
            Lobby = lobby;
        }

        /// <summary>Starts watching for the game window and capturing it, in the background.</summary>
        public static GameCapture Start(Replay lobby)
        {
            var game = new GameCapture(lobby);
            Task.Run(game.RunAsync);
            return game;
        }

        /// <summary>The kept frames, oldest first.</summary>
        public IReadOnlyList<CapturedFrame> Frames
        {
            get {
                lock (_lock) {
                    return _frames.ToList();
                }
            }
        }

        private async Task RunAsync()
        {
            try {
                if (!WindowCapture.IsSupported) {
                    _log.Warn("Rank reading: this version of Windows can't capture windows (needs Windows 10 2004 or later)");
                    return;
                }

                // The lobby file can appear a moment before the game has its window up.
                var window = IntPtr.Zero;
                while (window == IntPtr.Zero && !_stop.IsCancellationRequested) {
                    window = WindowCapture.FindGameWindow();
                    if (window == IntPtr.Zero) {
                        await Task.Delay(TimeSpan.FromSeconds(1), _stop.Token);
                    }
                }

                lock (_lock) {
                    if (_stop.IsCancellationRequested) {
                        return;
                    }
                    _capture = WindowCapture.Start(window, Interval, OnFrame);
                }
                _log.Debug("Rank reading: capturing the game window until the match starts");

                await Task.Delay(MaxWait, _stop.Token);
                _log.Info($"Rank reading: no match start within {MaxWait.TotalMinutes} minutes of the lobby - dropping what was captured");
                Dispose();
            }
            catch (OperationCanceledException) {
            }
            catch (Exception ex) {
                _log.Warn(ex, "Rank reading: couldn't capture the game window");
                Dispose();
            }
        }

        private void OnFrame(SoftwareBitmap bitmap)
        {
            var at = DateTime.UtcNow;
            Task.Run(async () => {
                try {
                    var width = bitmap.PixelWidth;
                    var height = bitmap.PixelHeight;
                    var strip = (uint)Math.Round(width * StripWidth);
                    var left = await WindowCapture.EncodePngAsync(bitmap, new BitmapBounds { X = 0, Y = 0, Width = strip, Height = (uint)height });
                    var right = await WindowCapture.EncodePngAsync(bitmap, new BitmapBounds { X = (uint)width - strip, Y = 0, Width = strip, Height = (uint)height });

                    lock (_lock) {
                        if (_stop.IsCancellationRequested) {
                            return;
                        }
                        _frames.Enqueue(new CapturedFrame(at, width, height, left, right));
                        while (_frames.Count > FramesKept) {
                            _frames.Dequeue();
                        }
                    }
                }
                catch (Exception ex) {
                    _log.Debug($"Rank reading: couldn't keep a frame: {ex.Message}");
                }
                finally {
                    bitmap.Dispose();
                }
            });
        }

        /// <summary>Stops capturing and drops every kept frame.</summary>
        public void Dispose()
        {
            lock (_lock) {
                if (!_stop.IsCancellationRequested) {
                    _stop.Cancel();
                }
                _capture?.Dispose();
                _capture = null;
            }
        }

        /// <summary>Stops capturing but keeps the frames, for sending.</summary>
        public IReadOnlyList<CapturedFrame> Finish()
        {
            lock (_lock) {
                var frames = _frames.ToList();
                if (!_stop.IsCancellationRequested) {
                    _stop.Cancel();
                }
                _capture?.Dispose();
                _capture = null;
                _frames.Clear();
                return frames;
            }
        }
    }

    /// <summary>One captured frame: when, the game's resolution, and the left and right card strips as PNG.</summary>
    internal sealed record CapturedFrame(DateTime At, int Width, int Height, byte[] LeftPng, byte[] RightPng);
}
