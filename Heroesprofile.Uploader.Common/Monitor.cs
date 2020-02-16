using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Heroesprofile.Uploader.Common
{
    public class Monitor : IMonitor
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        protected readonly string ProfilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"Heroes of the Storm\Accounts");
        protected FileSystemWatcher _watcher;

        /// <summary>
        /// Fires when a new replay file is found
        /// </summary>
        public event EventHandler<EventArgs<string>> ReplayAdded;
        protected virtual void OnReplayAdded(string path)
        {
            _log.Debug($"Detected new replay: {path}");
            ReplayAdded?.Invoke(this, new EventArgs<string>(path));
        }

        /// <summary>
        /// Starts watching filesystem for new replays. When found raises <see cref="ReplayAdded"/> event.
        /// </summary>
        public void Start()
        {
            if (_watcher == null) {
                _watcher = new FileSystemWatcher() {
                    Path = ProfilePath,
                    Filter = "*.StormReplay",
                    IncludeSubdirectories = true
                };
                _watcher.Created += (o, e) => OnReplayAdded(e.FullPath);
            }
            _watcher.EnableRaisingEvents = true;
            _log.Debug($"Started watching for new replays");
        }

        /// <summary>
        /// Stops watching filesystem for new replays
        /// </summary>
        public void Stop()
        {
            if (_watcher != null) {
                _watcher.EnableRaisingEvents = false;
            }
            _log.Debug($"Stopped watching for new replays");
        }

        /// <summary>
        /// Finds all available replay files
        /// </summary>
        public IEnumerable<string> ScanReplays()
        {
            return Directory.GetFiles(ProfilePath, "*.StormReplay", SearchOption.AllDirectories);
        }
    }
}
