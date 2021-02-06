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
        //protected readonly string BattleLobbyTempPath = Path.Combine(Path.GetTempPath(), @"Heroes of the Storm\");
        protected readonly string BattleLobbyTempPath = Path.GetTempPath();
        protected readonly string StormSavePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"Heroes of the Storm\Accounts");
        protected FileSystemWatcher _battlelobby_watcher;
        protected FileSystemWatcher _stormsave_watcher;

        /// <summary>
        /// Fires when a new replay file is found
        /// </summary>
        public event EventHandler<EventArgs<string>> TempBattleLobbyCreated;
        public event EventHandler<EventArgs<string>> StormSaveCreated;

        protected virtual void OnBattleLobbyAdded(object source, FileSystemEventArgs e)
        {
            _log.Debug($"Detected new temp live replay: {e.FullPath}");
            TempBattleLobbyCreated?.Invoke(this, new EventArgs<string>(e.FullPath));
        }


        protected virtual void OnStormSaveAdded(object source, FileSystemEventArgs e)
        {
            _log.Debug($"Detected new StormSave replay: {e.FullPath}");
            StormSaveCreated?.Invoke(this, new EventArgs<string>(e.FullPath));
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
            if (_stormsave_watcher == null) {
                _stormsave_watcher = new FileSystemWatcher() {
                    Path = StormSavePath,
                    Filter = "*.StormSave",
                    IncludeSubdirectories = true
                };
                _stormsave_watcher.Created -= OnStormSaveAdded;
                _stormsave_watcher.Created += OnStormSaveAdded;
            }
            _stormsave_watcher.EnableRaisingEvents = true;

            _log.Debug($"Started watching for new storm save");
        }


        /// <summary>
        /// Stops watching filesystem for new replays
        /// </summary>
        public void StopBattleLobbyWatcher()
        {
            if (_battlelobby_watcher != null) {
                _battlelobby_watcher.EnableRaisingEvents = false;
            }
            _log.Debug($"Stopped watching for new replays");
        }

        public void StopStormSaveWatcher()
        {
            if (_stormsave_watcher != null) {
                _stormsave_watcher.EnableRaisingEvents = false;
            }
            _log.Debug($"Stopped watching for new storm save files");
        }

        public bool IsBattleLobbyRunning()
        {
            return _battlelobby_watcher == null ? false : true;
        }

        public bool IsStormSaveRunning()
        {
            return _stormsave_watcher == null ? false : true;
        }
    }
}
