using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Heroesprofile.Uploader.Common
{
    public class LiveMonitor : ILiveMonitor
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Overrides where the battle lobby watcher looks for .battlelobby files. Windows needs no
        /// override - it's Path.GetTempPath(). Under Wine/Proton on Linux the game writes it inside the
        /// prefix instead, at drive_c\users\*\AppData\Local\Temp\, so the resolved prefix path has to be
        /// set here before the watchers are started.
        /// </summary>
        public static string BattleLobbyTempPathOverride { get; set; }

        protected string BattleLobbyTempPath { get { return BattleLobbyTempPathOverride ?? Path.GetTempPath(); } }
        protected string StormSavePath { get { return ReplayLocation.Current; } }
        protected FileSystemWatcher _battlelobby_watcher;
        protected FileSystemWatcher _stormsave_watcher;
        private string _watchedStormSavePath;

        /// <summary>
        /// The game keeps one save per game and rewrites it as the match goes on. On most
        /// machines that arrives as Created, on some only as Changed, and FileSystemWatcher
        /// drops events under load on any of them - hence both handlers and the poll below.
        /// </summary>
        private static readonly TimeSpan StormSaveCooldown = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan StormSavePollInterval = TimeSpan.FromSeconds(10);

        private readonly object _stormSaveLock = new object();
        private readonly Dictionary<string, DateTime> _stormSaveSeen = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private System.Threading.Timer _stormsave_poll;
        private string _polledPath;
        private DateTime _polledWrite = DateTime.MinValue;

        public event EventHandler<EventArgs<string>> TempBattleLobbyCreated;
        public event EventHandler<EventArgs<string>> StormSaveCreated;

        protected virtual void OnBattleLobbyAdded(object source, FileSystemEventArgs e)
        {
            _log.Debug($"Detected new temp live replay: {e.FullPath}");
            TempBattleLobbyCreated?.Invoke(this, new EventArgs<string>(e.FullPath));
        }


        protected virtual void OnStormSaveAdded(object source, FileSystemEventArgs e)
        {
            RaiseStormSave(e.FullPath, e.ChangeType.ToString());
        }

        private void RaiseStormSave(string path, string reason)
        {
            lock (_stormSaveLock) {
                DateTime last;
                if (_stormSaveSeen.TryGetValue(path, out last) && DateTime.UtcNow - last < StormSaveCooldown) {
                    return;
                }
                _stormSaveSeen[path] = DateTime.UtcNow;
            }

            _log.Debug($"Detected StormSave replay ({reason}): {path}");
            StormSaveCreated?.Invoke(this, new EventArgs<string>(path));
        }

        /// <summary>
        /// Backstop for the watcher: the newest save on disk, whenever it has been written
        /// since we last looked. Seeded on start so a save left over from an earlier session
        /// is not replayed.
        /// </summary>
        private void PollStormSaves(object state)
        {
            try {
                var newest = NewestStormSave();
                if (newest == null) {
                    return;
                }

                if (newest.FullName == _polledPath && newest.LastWriteTimeUtc <= _polledWrite) {
                    return;
                }

                _polledPath = newest.FullName;
                _polledWrite = newest.LastWriteTimeUtc;

                // The watcher usually got there first: a write it reported - the last of that burst
                // landing within the cooldown of the event - is the one already read, not a new one.
                // Without this every save the watcher catches is read a second time by the next poll.
                lock (_stormSaveLock) {
                    if (_stormSaveSeen.TryGetValue(newest.FullName, out var seen) && newest.LastWriteTimeUtc <= seen + StormSaveCooldown) {
                        return;
                    }
                }
                RaiseStormSave(newest.FullName, "poll");
            }
            catch (Exception ex) {
                _log.Debug($"Could not poll for storm saves: {ex.Message}");
            }
        }

        private FileInfo NewestStormSave()
        {
            var path = StormSavePath;

            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) {
                return null;
            }

            return new DirectoryInfo(path)
                .EnumerateFiles("*.StormSave", SearchOption.AllDirectories)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();
        }

        /// <summary>
        /// Starts watching filesystem for new battlelobby. When found raises <see cref="OnBattleLobbyAdded"/> event.
        /// </summary>
        public void StartBattleLobby()
        {
            if (_battlelobby_watcher == null) {
                // Directory.CreateDirectory(BattleLobbyTempPath);
                _battlelobby_watcher = new FileSystemWatcher() {
                    Path = BattleLobbyTempPath,
                    Filter = "*.battlelobby",
                    IncludeSubdirectories = true
                };
                _battlelobby_watcher.Changed -= OnBattleLobbyAdded;
                _battlelobby_watcher.Changed += OnBattleLobbyAdded;
            }
            _battlelobby_watcher.EnableRaisingEvents = true;

            _log.Debug($"Started watching for new battlelobby");
        }

        /// <summary>
        /// Starts watching filesystem for new storm saves. When found raises <see cref="OnStormSaveAdded"/> event.
        /// </summary>
        public void StartStormSave()
        {
            if (_stormsave_watcher != null && _watchedStormSavePath != StormSavePath) {
                // replay folder was changed in settings, rebuild the watcher around the new one
                _stormsave_watcher.EnableRaisingEvents = false;
                _stormsave_watcher.Created -= OnStormSaveAdded;
                _stormsave_watcher.Dispose();
                _stormsave_watcher = null;
            }
            if (_stormsave_watcher == null) {
                try {
                    _stormsave_watcher = new FileSystemWatcher() {
                        Path = StormSavePath,
                        Filter = "*.StormSave",
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
                    };
                    _stormsave_watcher.Created -= OnStormSaveAdded;
                    _stormsave_watcher.Created += OnStormSaveAdded;
                    // A save rewritten in place never raises Created, and that is the whole
                    // game's talents missing for anyone whose system does it that way.
                    _stormsave_watcher.Changed -= OnStormSaveAdded;
                    _stormsave_watcher.Changed += OnStormSaveAdded;
                    _watchedStormSavePath = StormSavePath;
                }
                catch (Exception ex) {
                    _log.Error(ex, $"Failed to watch replay directory for storm saves: {StormSavePath}");
                    _stormsave_watcher = null;
                    return;
                }
            }
            _stormsave_watcher.EnableRaisingEvents = true;

            if (_stormsave_poll == null) {
                var seed = NewestStormSave();
                _polledPath = seed?.FullName;
                _polledWrite = seed?.LastWriteTimeUtc ?? DateTime.MinValue;
                _stormsave_poll = new System.Threading.Timer(PollStormSaves, null, StormSavePollInterval, StormSavePollInterval);
            }

            _log.Debug($"Started watching for new storm save in {StormSavePath}");
        }


        /// <summary>
        /// Stops watching filesystem for new battlelobby
        /// </summary>
        public void StopBattleLobbyWatcher()
        {
            if (_battlelobby_watcher != null) {
                _battlelobby_watcher.EnableRaisingEvents = false;
            }
            _log.Debug($"Stopped watching for new battlelobby");
        }

        /// <summary>
        /// Stops watching filesystem for new stormsave
        /// </summary>
        public void StopStormSaveWatcher()
        {
            if (_stormsave_watcher != null) {
                _stormsave_watcher.EnableRaisingEvents = false;
            }

            if (_stormsave_poll != null) {
                _stormsave_poll.Dispose();
                _stormsave_poll = null;
            }

            _log.Debug($"Stopped watching for new storm save files");
        }

        /// <summary>
        /// Checks whether battlelobby watcher is running
        /// </summary>
        public bool IsBattleLobbyRunning()
        {
            return _battlelobby_watcher == null ? false : true;
        }

        /// <summary>
        /// Checks whether stormsave watcher is running
        /// </summary>
        public bool IsStormSaveRunning()
        {
            return _stormsave_watcher == null ? false : true;
        }
    }
}
