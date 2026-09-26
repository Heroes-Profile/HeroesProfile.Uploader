using NLog;
using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace Heroesprofile.Uploader.Desktop.Updates
{
    /// <summary>
    /// Velopack updates, on all three OSes - the GUI's equivalent of the WPF app's Squirrel
    /// UpdateManager: check, download in the background, then apply on "Restart now" (or, if the user
    /// never clicks it, automatically the next time the app starts - VelopackApp.Run in Program.Main).
    /// Only an installed copy can update itself (Setup.exe on Windows, the AppImage on Linux, the .app on
    /// macOS); a dev build or the Linux tarball reports <see cref="Outcome.NotInstalled"/>.
    /// </summary>
    internal sealed class AppUpdater
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        public enum Outcome
        {
            /// <summary>Not an installed copy, so Velopack can't update it - see <see cref="ReleaseChecker"/>.</summary>
            NotInstalled,
            NoUpdate,
            /// <summary>An update is downloaded and applies on restart.</summary>
            ReadyToRestart,
            Failed,
        }

        public sealed class Result
        {
            public Outcome Outcome { get; init; }
            public string Version { get; init; }
        }

        private UpdateManager _manager;
        private VelopackAsset _ready;

        /// <summary>
        /// Set to a local folder (or URL) holding `vpk pack` output to update from there instead of
        /// GitHub - for testing an update end to end, or a release candidate, before it's published.
        /// </summary>
        public const string SourceOverrideVariable = "HEROESPROFILE_UPLOADER_UPDATE_SOURCE";

        private static UpdateManager CreateManager(AppConfig config)
        {
            var overrideSource = Environment.GetEnvironmentVariable(SourceOverrideVariable);
            if (!string.IsNullOrWhiteSpace(overrideSource)) {
                _log.Info($"Updating from {overrideSource} ({SourceOverrideVariable}) instead of GitHub");
                return new UpdateManager(overrideSource);
            }
            return new UpdateManager(new GithubSource($"https://github.com/{config.UpdateRepository}", null, config.AllowPreReleases));
        }

        /// <summary>Checks the configured repository and, if there's a newer release, downloads it. Never throws.</summary>
        public async Task<Result> CheckAndDownloadAsync(AppConfig config)
        {
            try {
                var manager = CreateManager(config);
                if (!manager.IsInstalled) {
                    return new Result { Outcome = Outcome.NotInstalled };
                }

                // Already downloaded by an earlier check this run - nothing more to fetch.
                if (manager.UpdatePendingRestart is VelopackAsset pending) {
                    _manager = manager;
                    _ready = pending;
                    return new Result { Outcome = Outcome.ReadyToRestart, Version = pending.Version.ToString() };
                }

                var update = await manager.CheckForUpdatesAsync();
                if (update == null) {
                    return new Result { Outcome = Outcome.NoUpdate };
                }

                _log.Info($"Downloading update {update.TargetFullRelease.Version}");
                await manager.DownloadUpdatesAsync(update);
                _manager = manager;
                _ready = update.TargetFullRelease;
                _log.Info($"Update {_ready.Version} downloaded - it installs when the uploader restarts.");
                return new Result { Outcome = Outcome.ReadyToRestart, Version = _ready.Version.ToString() };
            }
            catch (Exception ex) {
                _log.Warn(ex, "Update check failed - will retry on the next check.");
                return new Result { Outcome = Outcome.Failed };
            }
        }

        /// <summary>
        /// Exits this process, applies the downloaded update and starts the new version (minimized if
        /// <paramref name="minimized"/>). Returns false, doing nothing, if there's no downloaded update.
        /// </summary>
        public bool ApplyAndRestart(bool minimized)
        {
            if (_manager == null || _ready == null) {
                return false;
            }
            _log.Info($"Restarting to install {_ready.Version}");
            _manager.ApplyUpdatesAndRestart(_ready, minimized ? new[] { "--minimized" } : Array.Empty<string>());
            return true;
        }
    }
}
