using NLog;
using System;
using System.IO;
using System.Runtime.Versioning;

namespace Heroesprofile.Uploader.Desktop.Migration
{
    /// <summary>
    /// Moves a Windows user over from the WPF app: on the first run (no config.json of our own yet), the
    /// settings and the upload history are copied into this app's own folder. After that nothing here
    /// depends on the WPF app's folder, so it can be uninstalled the normal way - its uninstaller deletes
    /// %APPDATA%\Heroesprofile, which only held the WPF app's copies by then.
    /// </summary>
    internal static class WpfMigration
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        private const string HistoryFile = "replays_v8.xml";

        /// <summary>%APPDATA%\Heroesprofile - the WPF app's settings folder (App.SettingsDir).</summary>
        public static string OldDataDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Heroesprofile");

        /// <summary>
        /// The settings to start from on this machine: a config.json an early beta of this app wrote into
        /// the WPF app's folder (before it had its own), or else the WPF app's own settings. False if
        /// there are neither.
        /// </summary>
        [SupportedOSPlatform("windows")]
        public static bool TryImportSettings(out AppConfig config, out string source)
        {
            var betaConfig = Path.Combine(OldDataDir, "config.json");
            if (File.Exists(betaConfig)) {
                try {
                    config = AppConfig.FromJson(File.ReadAllText(betaConfig));
                    source = betaConfig;
                    return true;
                }
                catch (Exception ex) {
                    _log.Warn(ex, $"Could not read {betaConfig} - importing the WPF app's settings instead");
                }
            }
            return WpfSettingsImporter.TryImport(out config, out source);
        }

        /// <summary>
        /// Copies the upload history (replays_v8.xml) from <paramref name="oldDir"/> into
        /// <paramref name="newDir"/>, unless there's nothing to copy or <paramref name="newDir"/> already
        /// has its own. Safe to call more than once. Returns true if it copied.
        /// </summary>
        public static bool CopyHistory(string oldDir, string newDir)
        {
            var from = Path.Combine(oldDir, HistoryFile);
            var to = Path.Combine(newDir, HistoryFile);
            if (!File.Exists(from) || File.Exists(to)) {
                return false;
            }
            Directory.CreateDirectory(newDir);
            File.Copy(from, to);
            _log.Info($"Copied the upload history from {from}");
            return true;
        }
    }
}
