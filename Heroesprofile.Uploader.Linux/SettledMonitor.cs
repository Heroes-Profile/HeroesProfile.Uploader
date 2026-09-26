using Heroesprofile.Uploader.Common;
using NLog;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Heroesprofile.Uploader.Linux
{
    /// <summary>
    /// A <see cref="Monitor"/> that waits for a newly-created replay to finish being written before
    /// raising <see cref="Monitor.ReplayAdded"/>. On Windows, HotS holds the file open while writing
    /// it, so Manager's own <c>EnsureFileAvailable</c> (a write-open test) naturally blocks until it's
    /// done. Linux has no mandatory file locking, so that same test succeeds instantly on a
    /// half-written replay - it then gets parsed too early, fails, and (pre-fix) sat at "In progress"
    /// forever. Polling for the size to stop changing is the Linux-side fix for that.
    /// </summary>
    internal class SettledMonitor : Monitor
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        // How long the file size must stay unchanged before we consider the write finished.
        private static readonly TimeSpan SettleWindow = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(300);
        // Give up waiting after this long and process the file anyway - Analyzer's safety net will
        // mark it UploadError (retried on next launch) rather than leaving it stuck forever.
        private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(60);

        protected override async void OnReplayAdded(object source, FileSystemEventArgs e)
        {
            try {
                await WaitUntilSettled(e.FullPath);
            }
            catch (Exception ex) {
                // Never let this take the watcher down - worst case we process the file a bit early.
                _log.Warn(ex, $"Error waiting for replay to finish writing, processing anyway: {e.FullPath}");
            }
            base.OnReplayAdded(source, e);
        }

        private static async Task WaitUntilSettled(string path)
        {
            var overall = Stopwatch.StartNew();
            var stableSince = Stopwatch.StartNew();
            long lastLength = -1;

            while (overall.Elapsed < MaxWait) {
                long length;
                try {
                    length = new FileInfo(path).Length;
                }
                catch (IOException) {
                    // still being created/renamed by the game - treat as "still changing"
                    length = -1;
                }

                if (length != lastLength) {
                    lastLength = length;
                    stableSince.Restart();
                } else if (stableSince.Elapsed >= SettleWindow) {
                    return;
                }

                await Task.Delay(PollInterval);
            }

            _log.Warn($"Replay didn't finish settling within {MaxWait.TotalSeconds}s, processing anyway: {path}");
        }
    }
}
