using Heroesprofile.Uploader.Common;
using Heroesprofile.Uploader.Desktop.Platform;
using NLog;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Xml.Linq;

namespace Heroesprofile.Uploader.Desktop.Migration
{
    /// <summary>
    /// One-time import of the WPF app's settings on the first run on Windows, so switching apps keeps
    /// everything the user had set. The WPF app keeps its settings in a .NET user.config, and copies it
    /// to %APPDATA%\Heroesprofile\last.config on every exit and update (App.BackupSettings) - that
    /// copy is read first, since Squirrel leaves one user.config per installed version behind.
    ///
    /// Not imported: "Start with windows". The WPF app's Startup-folder shortcut keeps starting the WPF
    /// app until it's replaced; registering this app too would start both at login.
    /// </summary>
    internal static class WpfSettingsImporter
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        private const string SettingsSection = "Heroesprofile.Uploader.Windows.Properties.Settings";

        /// <summary>Finds and imports the WPF app's settings. False when there are none (or they can't be read).</summary>
        [SupportedOSPlatform("windows")]
        public static bool TryImport(out AppConfig config, out string source)
        {
            config = null;
            source = FindSettingsFile();
            if (source == null) {
                return false;
            }
            try {
                config = FromUserConfig(File.ReadAllText(source), WindowsPlatform.UnprotectFromBase64);
                return true;
            }
            catch (Exception ex) {
                _log.Warn(ex, $"Could not import the Windows app's settings from {source}");
                return false;
            }
        }

        /// <summary>last.config if the WPF app left one, otherwise the most recently written user.config.</summary>
        private static string FindSettingsFile()
        {
            var backup = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Heroesprofile", "last.config");
            if (File.Exists(backup)) {
                return backup;
            }

            // %LOCALAPPDATA%\Heroesprofile\Heroesprofile.Uploader.ex_Url_<hash>\<version>\user.config
            var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Heroesprofile");
            if (!Directory.Exists(local)) {
                return null;
            }
            return Directory.EnumerateDirectories(local, "Heroesprofile.Uploader.*")
                .SelectMany(dir => Directory.EnumerateFiles(dir, "user.config", SearchOption.AllDirectories))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }

        /// <summary>
        /// Builds a config from the text of a WPF user.config/last.config. <paramref name="unprotectTwitchKey"/>
        /// decrypts the WPF app's DPAPI-protected Twitch key (base64). Values that don't parse are skipped,
        /// leaving the default.
        /// </summary>
        public static AppConfig FromUserConfig(string xml, Func<string, string> unprotectTwitchKey)
        {
            var section = XDocument.Parse(xml).Descendants(SettingsSection).FirstOrDefault()
                ?? throw new FormatException($"No {SettingsSection} section - not the WPF uploader's settings file.");

            var values = section.Elements("setting").ToDictionary(
                s => (string)s.Attribute("name"),
                s => (string)s.Element("value") ?? "");

            string Get(string name) => values.TryGetValue(name, out var v) ? v.Trim() : null;
            void Bool(string name, Action<bool> set)
            {
                if (bool.TryParse(Get(name), out var b)) {
                    set(b);
                }
            }
            void Int(string name, Action<int> set)
            {
                if (int.TryParse(Get(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) {
                    set(i);
                }
            }

            var config = new AppConfig();

            var replayPath = Get("ReplayPath");
            config.ReplayPath = string.IsNullOrEmpty(replayPath) ? null : replayPath;
            Bool("PreMatchPage", v => config.PreMatchPage = v);
            Bool("PostMatchPage", v => config.PostMatchPage = v);
            config.WebhookUrl = Get("WebhookUrl") ?? "";
            Bool("TwitchExtension", v => config.TwitchExtension = v);
            Bool("MinimizeToTray", v => config.MinimizeToTray = v);
            Bool("AutoUpdate", v => config.AutoUpdate = v);
            Bool("AllowPreReleases", v => config.AllowPreReleases = v);

            if (Get("Theme") is string theme && theme != "") {
                config.Theme = theme;
            }
            if (Get("UpdateRepository") is string repo && repo != "") {
                config.UpdateRepository = repo;
            }
            if (Enum.TryParse<DeleteFiles>(Get("DeleteAfterUpload"), out var delete)) {
                config.DeleteAfterUpload = delete;
            }

            Int("WindowLeft", v => config.WindowLeft = v);
            Int("WindowTop", v => config.WindowTop = v);
            Int("WindowWidth", v => config.WindowWidth = v);
            Int("WindowHeight", v => config.WindowHeight = v);

            var protectedKey = Get("TwitchUploaderKey");
            config.TwitchUploaderKey = string.IsNullOrEmpty(protectedKey) ? "" : unprotectTwitchKey(protectedKey) ?? "";

            return config;
        }
    }
}
