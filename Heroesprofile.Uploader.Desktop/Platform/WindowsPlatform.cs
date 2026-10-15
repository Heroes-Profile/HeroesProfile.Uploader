using Microsoft.Win32;
using NLog;
using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Heroesprofile.Uploader.Desktop.Platform
{
    [SupportedOSPlatform("windows")]
    internal sealed class WindowsPlatform : IPlatform
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "Heroesprofile Uploader";

        private readonly string _home;

        /// <param name="home">Folder for config, data and the socket instead of the usual places, or null (see Platforms.HomeOverrideVariable).</param>
        public WindowsPlatform(string home = null)
        {
            _home = home;
        }

        public string Name => "Windows";

        /// <summary>
        /// This app's own folder - not the WPF app's %APPDATA%\Heroesprofile. The first run copies the WPF
        /// app's upload history (and settings) in from there (see Migration.WpfMigration), after which the
        /// old app can be uninstalled normally, taking its folder with it.
        /// </summary>
        public const string FolderName = "HeroesProfileUploader";

        public string ConfigDir => DataDir;
        public string DataDir => _home ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), FolderName);

        public string DefaultReplayFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Heroes of the Storm", "Accounts");

        // %TEMP%, LiveMonitor's own default - the game runs natively and writes it there.
        public string DefaultBattleLobbyFolder => null;

        public bool ReplayPathIsWinePrefix => false;
        public bool UseSettledMonitor => false;

        public bool SupportsStartOnLogin => true;

        public void SetStartOnLogin(bool enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (enabled) {
                key.SetValue(RunValueName, $"\"{Environment.ProcessPath}\" --minimized");
            } else {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
        }

        // Local rather than roaming AppData - a socket file has no business following the user around.
        public string SingleInstanceSocketPath => _home != null
            ? Path.Combine(_home, "instance.sock")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderName, "instance.sock");

        // Marks a DPAPI value in config.json, so a plain value (hand-edited, or copied from a Linux
        // config) is still recognised and read as-is.
        private const string DpapiPrefix = "dpapi:";

        public string ProtectSecret(string secret)
        {
            return string.IsNullOrEmpty(secret) ? "" : DpapiPrefix + ProtectToBase64(secret);
        }

        public string UnprotectSecret(string stored)
        {
            if (string.IsNullOrEmpty(stored)) {
                return "";
            }
            return stored.StartsWith(DpapiPrefix, StringComparison.Ordinal)
                ? UnprotectFromBase64(stored.Substring(DpapiPrefix.Length))
                : stored;
        }

        /// <summary>DPAPI-encrypts for the current Windows user, as base64 - exactly what the WPF app stored in user.config.</summary>
        internal static string ProtectToBase64(string secret) =>
            Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), null, DataProtectionScope.CurrentUser));

        /// <summary>
        /// Decrypts <see cref="ProtectToBase64"/>'s output, including the WPF app's stored Twitch key.
        /// Empty if it can't be (encrypted by another user or on another machine) - the user re-enters it.
        /// </summary>
        internal static string UnprotectFromBase64(string base64)
        {
            try {
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(base64), null, DataProtectionScope.CurrentUser));
            }
            catch (Exception ex) when (ex is CryptographicException || ex is FormatException) {
                _log.Warn(ex, "Could not decrypt the stored Twitch uploader key; it needs entering again");
                return "";
            }
        }
    }
}
