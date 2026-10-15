using NLog;
using System;
using System.IO;

namespace Heroesprofile.Uploader.Common
{
    /// <summary>
    /// Single source of truth for the HotS "Accounts" folder holding replays and storm saves.
    /// Defaults to the standard Documents location, but can be overridden by the user for setups where
    /// that cannot be resolved automatically - a relocated Documents folder, or a Wine/Proton prefix on Linux.
    /// </summary>
    public static class ReplayLocation
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Standard location, used when no override is configured
        /// </summary>
        public static readonly string DefaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Heroes of the Storm", "Accounts");

        private static string _customPath;

        /// <summary>
        /// Fires when <see cref="Current"/> changes, so watchers can be pointed at the new folder
        /// </summary>
        public static event EventHandler Changed;

        /// <summary>
        /// User configured override. Empty falls back to <see cref="DefaultPath"/>.
        /// </summary>
        public static string CustomPath
        {
            get {
                return _customPath;
            }
            set {
                var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                if (_customPath == normalized) {
                    return;
                }
                _customPath = normalized;
                _log.Info($"Replay folder set to {Current}");
                Changed?.Invoke(null, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Folder currently watched and scanned for replays
        /// </summary>
        public static string Current
        {
            get {
                return _customPath ?? DefaultPath;
            }
        }

        /// <summary>
        /// Whether <see cref="Current"/> exists and is readable. A missing or unreadable folder is a
        /// configuration problem, not a crash - the user is expected to point us at the right one.
        /// </summary>
        public static bool IsAvailable()
        {
            try {
                return Directory.Exists(Current);
            }
            catch (Exception ex) {
                _log.Error(ex, $"Failed to access replay directory: {Current}");
                return false;
            }
        }
    }
}
