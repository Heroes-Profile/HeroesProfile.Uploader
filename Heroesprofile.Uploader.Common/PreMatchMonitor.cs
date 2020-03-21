using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Heroesprofile.Uploader.Common
{
    public class PreMatchMonitor : PreMatchIMonitor
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        protected readonly string BattleLobbyTempPath = Path.Combine(Path.GetTempPath(), @"Heroes of the Storm\");
        protected readonly string StormSavePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"Heroes of the Storm\Accounts");
        protected FileSystemWatcher _battlelobby_watcher;
        protected FileSystemWatcher _stormsave_watcher;

        /// <summary>
        /// Fires when a new replay file is found
        /// </summary>
        public event EventHandler<EventArgs<string>> TempBattleLobbyCreated;
        public event EventHandler<EventArgs<string>> StormSaveCreated;
        protected virtual void OnBattleLobbyAdded(string path)
        {
            _log.Debug($"Detected new temp prematch replay: {path}");
            TempBattleLobbyCreated?.Invoke(this, new EventArgs<string>(path));
        }

        protected virtual void OnStormSaveAdded(string path)
        {
            _log.Debug($"Detected new StormSave replay: {path}");
            StormSaveCreated?.Invoke(this, new EventArgs<string>(path));
        }

        /// <summary>
        /// Starts watching filesystem for new replays. When found raises <see cref="TempReplayCreated"/> event.
        /// </summary>
        public void Start()
        {
            if (_battlelobby_watcher == null) {
                System.IO.Directory.CreateDirectory(BattleLobbyTempPath);
                _battlelobby_watcher = new FileSystemWatcher() {
                    Path = BattleLobbyTempPath,
                    Filter = "*.battlelobby",
                    IncludeSubdirectories = true
                };
                _battlelobby_watcher.Created += (o, e) => OnBattleLobbyAdded(e.FullPath);
            }
            _battlelobby_watcher.EnableRaisingEvents = true;

            if (_stormsave_watcher == null) {
                _stormsave_watcher = new FileSystemWatcher() {
                    Path = StormSavePath,
                    Filter = "*.StormSave",
                    IncludeSubdirectories = true
                };
                _stormsave_watcher.Created += (o, e) => OnStormSaveAdded(e.FullPath);
            }
            _stormsave_watcher.EnableRaisingEvents = true;

            _log.Debug($"Started watching for new stormsave replays");
        }

        /// <summary>
        /// Stops watching filesystem for new replays
        /// </summary>
        public void Stop()
        {
            if (_battlelobby_watcher != null) {
                _battlelobby_watcher.EnableRaisingEvents = false;
            }

            if (_stormsave_watcher != null) {
                _stormsave_watcher.EnableRaisingEvents = false;
            }
            _log.Debug($"Stopped watching for new replays");
        }
    }
}
